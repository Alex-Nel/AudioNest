using Avalonia.Media;

namespace AudioNest.Desktop;

internal sealed record TimelinePalette(IBrush Surface, IBrush Alternate, IBrush Grid, IBrush Text,
    IBrush Muted, IBrush Clip, IBrush ClipSelected, IBrush Wave, IBrush Accent)
{
    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
    private static readonly TimelinePalette Light = new(
        Brush("#FFFFFF"), Brush("#FAFAFA"), Brush("#DEDFE1"), Brush("#202124"), Brush("#666A70"),
        Brush("#E2E8ED"), Brush("#D1E3F3"), Brush("#466883"), Brush("#2864A0"));
    private static readonly TimelinePalette Dark = new(
        Brush("#202124"), Brush("#252629"), Brush("#45474B"), Brush("#F0F0F0"), Brush("#B4B7BE"),
        Brush("#3C454E"), Brush("#3C566F"), Brush("#B3CDE3"), Brush("#8CBDFA"));
    public static TimelinePalette For(MainViewModel vm) => vm.IsDarkMode ? Dark : Light;
}
