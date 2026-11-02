using Dawnforge;
using Dawnforge.Gui;

if (args.Length == 0)
{
    return GuiApp.Run();
}

WindowsConsole.AttachToParent();
return await Cli.Run(args);
