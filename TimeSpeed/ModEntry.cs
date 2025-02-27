using System;
using cantorsdust.Common;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using TimeSpeed.Framework;

namespace TimeSpeed;

/// <summary>The entry class called by SMAPI.</summary>
internal class ModEntry : Mod
{
    /*********
    ** Properties
    *********/
    /// <summary>Displays messages to the user.</summary>
    private Notifier Notifier;

    /// <summary>Provides helper methods for tracking time flow.</summary>
    private readonly TimeHelper TimeHelper = new();

    /// <summary>The mod configuration.</summary>
    private ModConfig Config;

    /// <summary>Whether the player has manually frozen (<c>true</c>) or resumed (<c>false</c>) time.</summary>
    private bool? ManualFreeze;

    /// <summary>The reason time would be frozen automatically if applicable, regardless of <see cref="ManualFreeze"/>.</summary>
    private AutoFreezeReason AutoFreeze = AutoFreezeReason.None;

    /// <summary>Whether time should be frozen.</summary>
    private bool IsTimeFrozen =>
        this.ManualFreeze == true
        || (this.AutoFreeze != AutoFreezeReason.None && this.ManualFreeze != false);


    /// <summary>Get whether time features should be enabled.</summary>
    private bool IsWorldReadyAndHost => Context.IsWorldReady && Context.IsMainPlayer;

    /// <summary>Whether the flow of time should be adjusted.</summary>
    private bool AdjustTime;

    /// <summary>Backing field for <see cref="TickInterval"/>.</summary>
    private int _tickInterval;

    /// <summary>The number of milliseconds per 10-game-minutes to apply.</summary>
    private int TickInterval
    {
        get => this._tickInterval;
        set => this._tickInterval = Math.Max(value, 0);
    }


    /*********
    ** Public methods
    *********/
    /// <inheritdoc />
    public override void Entry(IModHelper helper)
    {
        I18n.Init(helper.Translation);
        CommonHelper.RemoveObsoleteFiles(this, "TimeSpeed.pdb");

        // read config
        this.Config = helper.ReadConfig<ModConfig>();
        this.Notifier = new Notifier(this.Helper, this.ModManifest);

        // add time events
        this.TimeHelper.WhenTickProgressChanged(this.OnTickProgressed);
        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
        helper.Events.GameLoop.TimeChanged += this.OnTimeChanged;
        helper.Events.GameLoop.DayStarted += this.OnDayStarted;
        helper.Events.Input.ButtonsChanged += this.OnButtonsChanged;
        helper.Events.Player.Warped += this.OnWarped;
        helper.Events.Multiplayer.ModMessageReceived += this.OnModMessageReceived;

        // add time freeze/unfreeze notification
        {
            bool wasPaused = false;
            helper.Events.Display.RenderingHud += (_, _) =>
            {
                wasPaused = Game1.paused;
                if (this.IsTimeFrozen)
                    Game1.paused = true;
            };

            helper.Events.Display.RenderedHud += (_, _) =>
            {
                Game1.paused = wasPaused;
            };
        }
    }


    /*********
    ** Private methods
    *********/
    /****
    ** Event handlers
    ****/
    /// <inheritdoc cref="IGameLoopEvents.GameLaunched"/>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event arguments.</param>
    private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
    {
        this.RenderGenericModConfigMenu();
    }

    /// <inheritdoc cref="IGameLoopEvents.SaveLoaded"/>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event arguments.</param>
    private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
    {
        this.RenderGenericModConfigMenu();
    }

    /// <summary>Handles incoming commands from other farmhands wanting to control time.</summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void OnModMessageReceived(object sender, ModMessageReceivedEventArgs e)
    {
        if (e.FromModID == this.ModManifest.UniqueID)
        {
            // Messages for the host
            if (Context.IsMainPlayer)
            {
                if (e.Type == MessageType.ToggleFreeze) this.ToggleFreeze();
                else if (e.Type == MessageType.IncreaseTickInterval) this.ChangeTickInterval(true, e.ReadAs<int>());
                else if (e.Type == MessageType.DecreaseTickInterval) this.ChangeTickInterval(false, e.ReadAs<int>());
            }
            // Messages for the farmhands
            else
            {
                if (e.Type == MessageType.QuickNotify) this.Notifier.QuickNotify(e.ReadAs<string>());
                else if (e.Type == MessageType.ShortNotify) this.Notifier.ShortNotify(e.ReadAs<string>());
            }
        }
    }

    /// <inheritdoc cref="IGameLoopEvents.DayStarted"/>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event arguments.</param>
    private void OnDayStarted(object sender, DayStartedEventArgs e)
    {
        if (!this.IsWorldReadyAndHost)
            return;

        this.UpdateScaleForDay(Game1.season, Game1.dayOfMonth);
        this.UpdateTimeFreeze(clearPreviousOverrides: true);
        this.UpdateSettingsForLocation(Game1.currentLocation);
    }

    /// <inheritdoc cref="IInputEvents.ButtonsChanged"/>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event arguments.</param>
    private void OnButtonsChanged(object sender, ButtonsChangedEventArgs e)
    {
        // Verify we should be checking user input
        if (!Context.IsWorldReady // World not loaded
            || (!Context.IsPlayerFree && !Game1.eventUp) // player not free (except events)
            || Game1.keyboardDispatcher.Subscriber is not null) // textbox active
            return;
        
        if (this.Config.Keys.FreezeTime.JustPressed())
            this.ToggleFreeze();
        else if (this.Config.Keys.IncreaseTickInterval.JustPressed())
            this.ChangeTickInterval(increase: true);
        else if (this.Config.Keys.DecreaseTickInterval.JustPressed())
            this.ChangeTickInterval(increase: false);
        else if (this.Config.Keys.ReloadConfig.JustPressed())
            this.ReloadConfig();
    }

    /// <inheritdoc cref="IPlayerEvents.Warped"/>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event arguments.</param>
    private void OnWarped(object sender, WarpedEventArgs e)
    {
        if (!this.IsWorldReadyAndHost || !e.IsLocalPlayer)
            return;

        this.UpdateSettingsForLocation(e.NewLocation);
    }

    /// <inheritdoc cref="IGameLoopEvents.TimeChanged"/>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event arguments.</param>
    private void OnTimeChanged(object sender, TimeChangedEventArgs e)
    {
        if (!this.IsWorldReadyAndHost)
            return;

        this.UpdateFreezeForTime();
    }

    /// <inheritdoc cref="IGameLoopEvents.UpdateTicked"/>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event arguments.</param>
    private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
    {
        if (!this.IsWorldReadyAndHost)
            return;

        this.TimeHelper.Update();

        if (e.IsOneSecond && this.Monitor.IsVerbose)
        {
            string timeFrozenLabel;
            if (this.ManualFreeze is true)
                timeFrozenLabel = ", frozen manually";
            else if (this.ManualFreeze is false)
                timeFrozenLabel = ", resumed manually";
            else if (this.IsTimeFrozen)
                timeFrozenLabel = $", frozen per {this.AutoFreeze}";
            else
                timeFrozenLabel = null;

            this.Monitor.Log($"Time is {Game1.timeOfDay}; {this.TimeHelper.TickProgress:P} towards {Utility.ModifyTime(Game1.timeOfDay, 10)} (tick interval: {this.TimeHelper.CurrentDefaultTickInterval}, {this.TickInterval / 10_000m:0.##}s/min{timeFrozenLabel})");
        }
    }

    /// <summary>Raised after the <see cref="Framework.TimeHelper.TickProgress"/> value changes.</summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event arguments.</param>
    private void OnTickProgressed(object sender, TickProgressChangedEventArgs e)
    {
        if (!this.IsWorldReadyAndHost)
            return;

        if (this.IsTimeFrozen)
            this.TimeHelper.TickProgress = e.TimeChanged ? 0 : e.PreviousProgress;
        else
        {
            if (!this.AdjustTime)
                return;
            if (this.TickInterval == 0)
                this.TickInterval = 1000;

            if (e.TimeChanged)
                this.TimeHelper.TickProgress = this.ScaleTickProgress(this.TimeHelper.TickProgress, this.TickInterval);
            else
                this.TimeHelper.TickProgress = e.PreviousProgress + this.ScaleTickProgress(e.NewProgress - e.PreviousProgress, this.TickInterval);
        }
    }

    /****
    ** Methods
    ****/

    /// <summary>Reload <see cref="Config"/> from the config file.</summary>
    private void ReloadConfig()
    {
        this.Config = this.Helper.ReadConfig<ModConfig>();
        this.UpdateScaleForDay(Game1.season, Game1.dayOfMonth);
        this.UpdateSettingsForLocation(Game1.currentLocation);
        this.Notifier.ShortNotify(I18n.Message_ConfigReloaded());
    }

    /// <summary>Register Generic Mod Config Menu</summary>
    private void RenderGenericModConfigMenu()
    {
        GenericModConfigMenuIntegration.Register(this.ModManifest, this.Helper.ModRegistry, this.Monitor,
            getConfig: () => this.Config,
            reset: () => this.Config = new(),
            save: () => {
                this.Helper.WriteConfig(this.Config);
                if (this.IsWorldReadyAndHost)
                    this.UpdateSettingsForLocation(Game1.currentLocation);
            }
        );
    }


    /// <summary>Increment or decrement the tick interval, taking into account the held modifier key if applicable.</summary>
    /// <param name="increase">Whether to increment the tick interval; else decrement.</param>
    private void ChangeTickInterval(bool increase, int change = 0)
    {
        // If no change specific or 0, we get the defaults
        if (change == 0)
        {
            change = 1000;
            KeyboardState state = Keyboard.GetState();
            if (state.IsKeyDown(Keys.LeftControl))
                change *= 100;
            else if (state.IsKeyDown(Keys.LeftShift))
                change *= 10;
            else if (state.IsKeyDown(Keys.LeftAlt))
                change /= 10;
        }

        // If not the host, notify the host to change the tick interval
        if (!Context.IsMainPlayer)
        {
            this.Helper.Multiplayer.SendMessage(change, MessageType.TickInterval(increase), modIDs: [this.ModManifest.UniqueID]);
            return;
        }

        // update tick interval
        if (!increase)
        {
            int minAllowed = Math.Min(this.TickInterval, change);
            this.TickInterval = Math.Max(minAllowed, this.TickInterval - change);
        }
        else
            this.TickInterval = this.TickInterval + change;

        // log change
        this.Notifier.QuickNotify(
            I18n.Message_SpeedChanged(seconds: this.TickInterval / 1000)
        );
        this.Monitor.Log($"Tick length set to {this.TickInterval / 1000d:0.##} seconds.", LogLevel.Info);
    }

    /// <summary>Toggle whether time is frozen.</summary>
    private void ToggleFreeze()
    {
        // If not the host, notify the server to ToggleFreeze
        if (!Context.IsMainPlayer)
        {
            this.Helper.Multiplayer.SendMessage("Toggle server freeze.", MessageType.ToggleFreeze, modIDs: new[] { this.ModManifest.UniqueID });
            return;
        }

        if (!this.IsTimeFrozen)
        {
            this.UpdateTimeFreeze(manualOverride: true);
            this.Notifier.QuickNotify(I18n.Message_TimeStopped());
            this.Monitor.Log("Time is frozen globally.", LogLevel.Info);
        }
        else
        {
            this.UpdateTimeFreeze(manualOverride: false);
             this.Notifier.QuickNotify(I18n.Message_TimeResumed());
            this.Monitor.Log($"Time is resumed at \"{Game1.currentLocation.Name}\".", LogLevel.Info);
        }
    }

    /// <summary>Update the time freeze settings for the given time of day.</summary>
    private void UpdateFreezeForTime()
    {
        bool wasFrozen = this.IsTimeFrozen;
        this.UpdateTimeFreeze();

        if (!wasFrozen && this.IsTimeFrozen)
        {
            this.Notifier.ShortNotify(I18n.Message_OnTimeChange_TimeStopped());
            this.Monitor.Log($"Time automatically set to frozen at {Game1.timeOfDay}.", LogLevel.Info);
        }
    }

    /// <summary>Update the time settings for the given location.</summary>
    /// <param name="location">The game location.</param>
    private void UpdateSettingsForLocation(GameLocation location)
    {
        if (location == null)
            return;

        // update time settings
        this.UpdateTimeFreeze();
        this.TickInterval = this.Config.GetMillisecondsPerMinute(location) * 10;

        // notify player
        if (this.Config.LocationNotify)
        {
            switch (this.AutoFreeze)
            {
                case AutoFreezeReason.FrozenAtTime when this.IsTimeFrozen:
                    this.Notifier.ShortNotify(I18n.Message_OnLocationChange_TimeStoppedGlobally());
                    break;

                case AutoFreezeReason.FrozenForLocation when this.IsTimeFrozen:
                    this.Notifier.ShortNotify(I18n.Message_OnLocationChange_TimeStoppedHere());
                    break;

                default:
                    this.Notifier.ShortNotify(I18n.Message_OnLocationChange_TimeSpeedHere(seconds: this.TickInterval / 1000));
                    break;
            }
        }
    }

    /// <summary>Update the <see cref="AutoFreeze"/> and <see cref="ManualFreeze"/> flags based on the current context.</summary>
    /// <param name="manualOverride">An explicit freeze (<c>true</c>) or unfreeze (<c>false</c>) requested by the player, if applicable.</param>
    /// <param name="clearPreviousOverrides">Whether to clear any previous explicit overrides.</param>
    private void UpdateTimeFreeze(bool? manualOverride = null, bool clearPreviousOverrides = false)
    {
        bool? wasManualFreeze = this.ManualFreeze;
        AutoFreezeReason wasAutoFreeze = this.AutoFreeze;

        // update auto freeze
        this.AutoFreeze = this.GetAutoFreezeType();

        // update manual freeze
        if (manualOverride.HasValue)
            this.ManualFreeze = manualOverride.Value;
        else if (clearPreviousOverrides)
            this.ManualFreeze = null;

        // clear manual unfreeze if it's no longer needed
        if (this.ManualFreeze == false && this.AutoFreeze == AutoFreezeReason.None)
            this.ManualFreeze = null;

        // log change
        if (wasAutoFreeze != this.AutoFreeze)
            this.Monitor.Log($"Auto freeze changed from {wasAutoFreeze} to {this.AutoFreeze}.");
        if (wasManualFreeze != this.ManualFreeze)
            this.Monitor.Log($"Manual freeze changed from {wasManualFreeze?.ToString() ?? "null"} to {this.ManualFreeze?.ToString() ?? "null"}.");
    }

    /// <summary>Update the time settings for the given date.</summary>
    /// <param name="season">The current season.</param>
    /// <param name="dayOfMonth">The current day of month.</param>
    private void UpdateScaleForDay(Season season, int dayOfMonth)
    {
        this.AdjustTime = this.Config.ShouldScale(season, dayOfMonth);
    }

    /// <summary>Get the adjusted progress towards the next 10-game-minute tick.</summary>
    /// <param name="progress">The percentage of the clock tick interval (i.e. the interval between time changes) that elapsed since the last update tick.</param>
    /// <param name="newTickInterval">The clock tick interval to which to apply the progress.</param>
    private double ScaleTickProgress(double progress, int newTickInterval)
    {
        double ratio = this.TimeHelper.CurrentDefaultTickInterval / (newTickInterval * 1d); // ratio between the game's normal interval (e.g. 7000) and the player's custom interval
        return progress * ratio;
    }

    /// <summary>Get the freeze type which applies for the current context, ignoring overrides by the player.</summary>
    private AutoFreezeReason GetAutoFreezeType()
    {
        if (this.Config.ShouldFreeze(Game1.currentLocation))
            return AutoFreezeReason.FrozenForLocation;

        if (this.Config.ShouldFreeze(Game1.timeOfDay))
            return AutoFreezeReason.FrozenAtTime;

        return AutoFreezeReason.None;
    }
}
