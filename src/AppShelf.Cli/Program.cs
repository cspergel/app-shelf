using AppShelf.Cli;

// Thin entry point — argument parsing and dispatch only; all logic lives in AppShelf.Core.
return await Dispatcher.RunAsync(args);
