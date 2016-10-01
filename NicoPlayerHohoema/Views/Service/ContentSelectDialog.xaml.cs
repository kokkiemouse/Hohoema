﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Content Dialog item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace NicoPlayerHohoema.Views.Service
{
	public sealed partial class ContentSelectDialog : ContentDialog
	{
		public ContentSelectDialog()
		{
			this.InitializeComponent();
		}

		
	}


	public class ContentSelectContainerTemplateSelector : DataTemplateSelector
	{
		public DataTemplate List { get; private set; }
		public DataTemplate InputText { get; private set; }


		protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
		{
			if (item is Views.Service.ChoiceFromListSelectableContainer)
			{
				return List;
			}
			else if (item is Views.Service.TextInputSelectableContainer)
			{
				return InputText;
			}

			return base.SelectTemplateCore(item, container);
		}
	}
}
