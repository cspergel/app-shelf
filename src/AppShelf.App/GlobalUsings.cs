// WPF + WinForms are both referenced (WinForms only for the tray NotifyIcon). Their implicit
// global usings make several type names ambiguous; bind the bare names to the WPF types.
global using Application = System.Windows.Application;
global using MessageBox = System.Windows.MessageBox;
global using Clipboard = System.Windows.Clipboard;
global using Color = System.Windows.Media.Color;
global using Brush = System.Windows.Media.Brush;
// Drag-drop (CardDragDrop / DragAdorners) needs the WPF variants of these — WinForms (tray) also
// defines them.
global using Point = System.Windows.Point;
global using DragEventArgs = System.Windows.DragEventArgs;
global using MouseEventArgs = System.Windows.Input.MouseEventArgs;
global using DragDropEffects = System.Windows.DragDropEffects;
global using DataObject = System.Windows.DataObject;
global using DragDrop = System.Windows.DragDrop;
global using Pen = System.Windows.Media.Pen;
global using Brushes = System.Windows.Media.Brushes;
global using Size = System.Windows.Size;
global using TextBox = System.Windows.Controls.TextBox;
// Cursor handling for the drag (move cursor on hover + during drag) — WinForms (tray) also defines
// these names.
global using Cursor = System.Windows.Input.Cursor;
global using Cursors = System.Windows.Input.Cursors;
global using GiveFeedbackEventArgs = System.Windows.GiveFeedbackEventArgs;
