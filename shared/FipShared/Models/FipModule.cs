namespace FipShared.Models;

public enum FipModule
{
    FAIT,
    FIRM,
    FORMS
}

public static class FipModuleExtensions
{
    public static string FullName(this FipModule module) => module switch
    {
        FipModule.FAIT  => "Fortress AI Tools",
        FipModule.FIRM  => "Fortress Intelligence & Risk Management",
        FipModule.FORMS => "Fortress Form Tools",
        _               => module.ToString()
    };

    public static string ShortName(this FipModule module) => module switch
    {
        FipModule.FAIT  => "FAIT",
        FipModule.FIRM  => "FIRM",
        FipModule.FORMS => "FORMS",
        _               => module.ToString()
    };

    public static string Url(this FipModule module) => module switch
    {
        FipModule.FAIT  => "https://fait.fortressintelligence.com",
        FipModule.FIRM  => "https://firm.fortressintelligence.com",
        FipModule.FORMS => "https://forms.fortressintelligence.com",
        _               => "#"
    };
}
