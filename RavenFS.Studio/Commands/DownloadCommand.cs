﻿using System;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using RavenFS.Studio.Infrastructure;

namespace RavenFS.Studio.Commands
{
	public class DownloadCommand :ICommand
	{
		private string FileName;
		public DownloadCommand(string fileName)
		{
			FileName = fileName;
		}
		public bool CanExecute(object parameter)
		{
			return true;
		}

		public void Execute(object parameter)
		{
			var fileDialog = new SaveFileDialog();
			var result = fileDialog.ShowDialog();
			if (result != true)
				return;

			var stream = fileDialog.OpenFile();
			var dispatcher = Deployment.Current.Dispatcher;
			ApplicationModel.Client.DownloadAsync(FileName,stream)
				.ContinueWith(task =>
				{
					dispatcher.InvokeAsync(() =>
					{
						stream.Flush();
						stream.Dispose();
					});
					task.Wait();
					return task;
				});

			Application.Current.Host.NavigationState += "?" + Guid.NewGuid();
		}

		public event EventHandler CanExecuteChanged;
	}
}
