using ArcShare.Server;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace ArcShare.Pages
{
	/// <summary>
	/// An empty page that can be used on its own or navigated to within a Frame.
	/// </summary>
	public sealed partial class ReceivePage : Page
	{
		public Frame MainFrame;

		public ReceivePage()
		{
			this.InitializeComponent();
		}

		private async void startButton_Click(object sender, RoutedEventArgs e)
		{
			this.IsEnabled = false;
			var loader = Windows.ApplicationModel.Resources.ResourceLoader.GetForCurrentView();
			//生成主页
			StorageFolder storageFolder = ApplicationData.Current.LocalFolder;
			StorageFile indexGenerateFile = await storageFolder.CreateFileAsync("index_r.html", CreationCollisionOption.ReplaceExisting);
			StorageFile indexFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Server/content/receive.html"));
			await indexFile.CopyAndReplaceAsync(indexGenerateFile);
			string content = await FileIO.ReadTextAsync(indexGenerateFile);
			//翻译内容
			content = content.Replace("[FileList]", loader.GetString("FileList")).Replace("[Size]", loader.GetString("Size"))
				.Replace("[Progress]", loader.GetString("Progress")).Replace("[BrowseFiles]", loader.GetString("BrowseFiles"))
				.Replace("[SelectAll]", loader.GetString("SelectAll")).Replace("[CancelSelection]", loader.GetString("CancelSelection"))
				.Replace("[RemoveSelected]", loader.GetString("RemoveSelected")).Replace("[Send]", loader.GetString("Send"))
				.Replace("[DescriptionShort]", loader.GetString("DescriptionShort")).Replace("[GetFromMicrosoft]", loader.GetString("GetFromMicrosoft"));
			//写入文件
			await FileIO.WriteTextAsync(indexGenerateFile, content, Windows.Storage.Streams.UnicodeEncoding.Utf8);
			//开启服务器
			HttpServer server = new HttpServer(AppSettings.PreferredPort, indexGenerateFile);
			AppSettings.CurrentServer = server;
			this.MainFrame.Navigate(typeof(RunningPage), null, new Windows.UI.Xaml.Media.Animation.DrillInNavigationTransitionInfo());
			this.IsEnabled = true;
		}
	}
}
