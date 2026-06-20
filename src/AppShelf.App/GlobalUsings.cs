// WPF + WinForms are both referenced (WinForms only for the tray NotifyIcon). Their implicit
// global usings make several type names ambiguous; bind the bare names to the WPF types.
global using Application = System.Windows.Application;
global using MessageBox = System.Windows.MessageBox;
global using Clipboard = System.Windows.Clipboard;
global using Color = System.Windows.Media.Color;
global using Brush = System.Windows.Media.Brush;
