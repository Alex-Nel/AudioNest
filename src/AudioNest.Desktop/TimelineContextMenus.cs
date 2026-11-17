using AudioNest.Domain;
using Avalonia.Controls;

namespace AudioNest.Desktop;

/// <summary>Shared contextual command construction; targets are captured, never inferred from a later selection.</summary>
public static class TimelineContextMenus
{
    public static ContextMenu TrackCreation(MainViewModel vm)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Add stereo track", vm.AddTrack, () => vm.CanEdit));
        menu.Items.Add(Item("Add mono track", vm.AddMonoTrack, () => vm.CanEdit));
        return menu;
    }
    public static ContextMenu Clip(MainViewModel vm, Guid track, AudioClip? clip, long frame)
    {
        var menu = new ContextMenu();
        void Select() => vm.Select(track, clip?.Id);
        if (clip is not null)
        {
            menu.Items.Add(Item("Cut", () => { Select(); vm.Cut(); }, () => vm.CanEdit));
            menu.Items.Add(Item("Copy", () => { Select(); vm.Copy(); }, () => vm.CanEdit));
        }
        if (clip is not null || vm.CanPaste)
            menu.Items.Add(Item("Paste", () => vm.PasteAt(track, frame), () => vm.CanPaste));
        if (clip is not null)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Split here", () => { Select(); vm.SplitAt(clip.Id, frame); },
                () => vm.CanEdit && frame > clip.Start && frame < clip.End));
        }
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        var mute = Item("Mute track", () => vm.ToggleTrackMute(track), () => vm.CanEdit);
        mute.ToggleType = MenuItemToggleType.CheckBox;
        mute.IsChecked = vm.Project.Tracks.First(t => t.Id == track).Muted;
        menu.Items.Add(mute);
        return menu;
    }
    private static MenuItem Item(string header, Action action, Func<bool> enabled) =>
        new() { Header = header, Command = new UiCommand(action, enabled) };
}
