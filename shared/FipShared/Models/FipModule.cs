namespace FipShared.Models;

public enum FipModule
{
    FAIT,
    FIRM,
    FORMS,
    Cowork,
    FAMOS = 4
}

public static class FipModuleExtensions
{
    public static string FullName(this FipModule module) => module switch
    {
        FipModule.FAIT   => "Fortress AI Tools",
        FipModule.FIRM   => "Fortress Intelligence & Risk Management",
        FipModule.FORMS  => "Fortress Form Tools",
        FipModule.Cowork => "FAIT Cowork",
        FipModule.FAMOS  => "FAM OS",
        _                => module.ToString()
    };

    public static string ShortName(this FipModule module) => module switch
    {
        FipModule.FAIT   => "FAIT",
        FipModule.FIRM   => "FIRM",
        FipModule.FORMS  => "FORMS",
        FipModule.Cowork => "Cowork",
        FipModule.FAMOS  => "FAM OS",
        _                => module.ToString()
    };

    public static string Url(this FipModule module) => module switch
    {
        FipModule.FAIT   => "https://fait.fortressintelligence.com",
        FipModule.FIRM   => "https://firm.fortressintelligence.com",
        FipModule.FORMS  => "https://forms.fortressintelligence.com",
        FipModule.Cowork => "https://cowork.fortressintelligence.com",
        FipModule.FAMOS  => "https://famos.fortressam.ai",
        _                => "#"
    };
}
