namespace Shield.Core.Domain;

// Generic key/value bag for runtime-mutable settings. Values are encrypted at rest via IDataProtector
// (purpose "shield.settings"); reads/writes flow through SettingsController.
public sealed class AppSetting
{
    public string Key { get; set; } = "";
    public string ValueEncrypted { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
