﻿using System.Threading.Tasks;
using Windows.UI.Xaml;

namespace NicoPlayerHohoema.Views.Extensions
{
    

    public partial class ListViewBase : DependencyObject
    {
        public interface IDeferInitialize
        {
            Task DeferInitializeAsync();
        }



        public static readonly DependencyProperty DeferInitializeProperty =
           DependencyProperty.RegisterAttached(
               "DeferInitialize",
               typeof(bool),
               typeof(ListViewBase),
               new PropertyMetadata(false, DeferInitializePropertyChanged)
           );

        public static void SetDeferInitialize(UIElement element, bool value)
        {
            element.SetValue(DeferInitializeProperty, value);
        }
        public static bool GetDeferInitialize(UIElement element)
        {
            return (bool)element.GetValue(DeferInitializeProperty);
        }


        private static void DeferInitializePropertyChanged(DependencyObject s, DependencyPropertyChangedEventArgs e)
        {
            if (s is Windows.UI.Xaml.Controls.ListViewBase target)
            {
                if ((bool)e.NewValue)
                {
                    target.ContainerContentChanging += Target_ContainerContentChanging;
                }
                else
                {
                    target.ContainerContentChanging -= Target_ContainerContentChanging;
                }
            }
        }

        private static async void Target_ContainerContentChanging(Windows.UI.Xaml.Controls.ListViewBase sender, Windows.UI.Xaml.Controls.ContainerContentChangingEventArgs args)
        {
            if (args.Item is IDeferInitialize updatable)
            {
                if (args.Phase != 0) { return; }

                await updatable.DeferInitializeAsync();

                // Handled = trueを指定すると、UIの描画が始まる模様
                // データ受信などが完了してない状態ではHandledを変更しない
                args.Handled = true;
            }
        }


    }
}
