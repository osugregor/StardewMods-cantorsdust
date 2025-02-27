using StardewModdingAPI;
using StardewValley;

namespace TimeSpeed.Framework;

/// <summary>Displays messages to the user in-game.</summary>
internal class Notifier
{
    public IModHelper Helper { get; internal set; }
    public IManifest ModManifest { get; internal set; }

    internal Notifier(IModHelper helper, IManifest modManifest)
    {
        this.Helper = helper;
        this.ModManifest = modManifest;
    }

    /*********
    ** Public methods
    *********/
    /// <summary>Display a message for one second.</summary>
    /// <param name="message">The message to display.</param>
    public void QuickNotify(string message)
    {
        Game1.addHUDMessage(new HUDMessage(message, HUDMessage.newQuest_type) { timeLeft = 1000 });
        this.Helper.Multiplayer.SendMessage(message, MessageType.QuickNotify, modIDs: [this.ModManifest.UniqueID]);
    }

    /// <summary>Display a message for two seconds.</summary>
    /// <param name="message">The message to display.</param>
    public void ShortNotify(string message)
    {
        Game1.addHUDMessage(new HUDMessage(message, HUDMessage.newQuest_type) { timeLeft = 2000 });
        this.Helper.Multiplayer.SendMessage(message, MessageType.ShortNotify, modIDs: [this.ModManifest.UniqueID]);
    }
}
