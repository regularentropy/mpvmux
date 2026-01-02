using System;
using System.Collections.Generic;
using System.Text;

namespace mpvmux.Services;

internal interface IDialogHelper
{
    void ShowError(string text);
    void ShowInfo(string text);
}

internal class DialogHelper(IWindowService ws) : IDialogHelper
{
    private readonly IWindowService _ws = ws;

    public void ShowInfo(string text)
    {
        _ws.ShowDialog(() => new MessageBoxWindow("Info", text));
    }

    public void ShowError(string text)
    {
        _ws.ShowDialog(() => new MessageBoxWindow("Error", text));
    }
}
