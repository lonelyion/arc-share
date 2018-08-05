using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Foundation.Collections;
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
	public sealed partial class SettingsPage : Page
	{
		bool flag = false;
		public SettingsPage()
		{
			this.InitializeComponent();
			this.Loaded += (sender, args) =>
			{
				switch (AppSettings.AppTheme)
				{
					case ApplicationTheme.Dark:
						RadioButtonDark.IsChecked = true;
						break;
					case ApplicationTheme.Light:
						RadioButtonLight.IsChecked = true;
						break;
					default:
						RadioButtonSystem.IsChecked = true;
						break;
				}
				if (AppSettings.DisplayLanguage == "zh-CN") LangComboBox.SelectedIndex = 0;
				else LangComboBox.SelectedIndex = 1;
				flag = true;
			};
		}

		private void ModeRadioButton_Checked(object sender, RoutedEventArgs e)
		{
			if (this == null) return;
			switch((sender as RadioButton).Tag.ToString())
			{
				case "0":
					AppSettings.AppTheme = ApplicationTheme.Light;
					break;
				case "1":
					AppSettings.AppTheme = ApplicationTheme.Dark;
					break;
				case "2":
					AppSettings.AppTheme = null;
					break;
			}
		}

		private void LangComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (flag)
			{ ////是用户的行为
				var selected = (LangComboBox.SelectedItem as TextBlock).Tag.ToString();
				Windows.ApplicationModel.Resources.Core.ResourceContext.SetGlobalQualifierValue("Language", selected);
				AppSettings.DisplayLanguage = selected;
			}
		}

		public string GetAppVersion()
		{
			Package package = Package.Current;
			PackageId packageId = package.Id;
			PackageVersion version = packageId.Version;
			return string.Format("{0}.{1}.{2}.{3}", version.Major, version.Minor, version.Build, version.Revision);
		}

		private async void HyperlinkButton_Click(object sender, RoutedEventArgs e)
		{
			LicensesDialog ld = new LicensesDialog();
			await ld.ShowAsync();
		}

		private async void HyperlinkButton_Click_1(object sender, RoutedEventArgs e)
		{
			var uri = new Uri("mailto:i@copperion.com");
			await Windows.System.Launcher.LaunchUriAsync(uri);
		}
	}
}
