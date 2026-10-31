// Project: Robin.Net
// File: Program.cs
// License: GNU General Public License version 3 (see LICENSE)

using RobinHood.Assets;
using RobinHood.Engine;
using RobinHood.Graphics;
using RobinHood.Platform;

var assets = AppContext.BaseDirectory;
if (!File.Exists(Path.Combine(assets, "ERULES.PRG")))
{
    Console.Error.WriteLine($"The Adventures of Robin Hood data files were not found beside the executable in {assets}.");
    Environment.ExitCode = 2;
    return;
}
var gameAssets = new GameAssets(assets);
var world = new GameWorld(gameAssets);
var saveDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RobinHoodNative");
var statusPath = Path.Combine(saveDirectory, GameWorld.StatusFileName);
world.SaveStatus = saved =>
{
    Directory.CreateDirectory(saveDirectory);
    File.WriteAllBytes(statusPath + ".tmp", saved.ExportStatus());
    File.Move(statusPath + ".tmp", statusPath, true);
};
world.DeleteStatus = () => { if (File.Exists(statusPath)) File.Delete(statusPath); };
if (File.Exists(statusPath))
{
    try { if (!world.ImportStatus(File.ReadAllBytes(statusPath))) Console.Error.WriteLine("Ignoring an incompatible " + statusPath); }
    catch (IOException) { Console.Error.WriteLine("Could not read " + statusPath); }
}
var graphics = new GameRenderer(world);
var biosSeed = unchecked((ushort)(uint)(DateTime.Now.TimeOfDay.TotalSeconds * 1193182 / 65536));
var rules = new RuleMachine(world, biosSeed);
var mouseDown = false;
var mouseX = 0;
var mouseY = 0;
using var window = new SdlWindow();
using var audio = world.Audio;
try { audio.Initialize(assets); }
catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
{
    Console.Error.WriteLine($"Audio disabled: {ex.Message}");
}
window.Run(delta =>
{
    world.AdvanceTime(delta);
    rules.Update(delta);
    return world.Ended ? null : graphics.Render();
}, input =>
{
    switch (input.Kind)
    {
        case InputKind.Key or InputKind.KeyUp:
            var scancode = KeyMap.ToScancode(input.Value);
            if (scancode != 0) world.StoreScancode(input.Kind == InputKind.Key ? scancode : (byte)(scancode | 0x80));
            break;
        case InputKind.Motion:
            mouseX = input.X; mouseY = input.Y;
            world.Mouse(mouseX, mouseY, mouseDown);
            break;
        case InputKind.Press or InputKind.Release when input.Value == 1:
            mouseDown = input.Kind == InputKind.Press;
            mouseX = input.X; mouseY = input.Y;
            world.Mouse(mouseX, mouseY, mouseDown);
            break;
    }
});
