﻿using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DiffViewer.Managers;
using Serilog;

namespace VSTSDataProvider.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    private ILogger _logger;
    public AboutViewModel(ILogger logger)
    {
        _logger = logger;
    }


    [RelayCommand]
    public void LoadAboutContent( )
    {
        _logger.Debug("LoadAboutContentCommand called");
        WeakReferenceMessenger.Default.Send(new DiffViewer.Messages.SetRichTextBoxDocumentMessage()
        {
            Sender = this ,
            Message = "LoadAboutContent" ,
            ObjReplied = new AboutManager().GenerateLicenseInfoDocument()
        });
    }

    /// <summary>
    /// Close Window by using WeakReferenceMessenger.
    /// </summary>
    [RelayCommand]
    public void CloseWindow( )
    {
        _logger.Debug("CloseWindowCommand called");
        WeakReferenceMessenger.Default.Send(new DiffViewer.Messages.WindowActionMessage() { Sender = this , Message = "Close" });
    }

}
