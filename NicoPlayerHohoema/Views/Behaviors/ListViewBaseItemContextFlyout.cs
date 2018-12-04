﻿using Microsoft.Xaml.Interactivity;
using Reactive.Bindings.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Gaming.Input;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;

namespace NicoPlayerHohoema.Views.Behaviors
{
    public class ListViewBaseItemContextFlyout : Behavior<ListViewBase>
    {
        CoreDispatcher _UIDispatcher;

        bool _IsAssignedDataContextToFlyout = false;

        protected override void OnAttached()
        {
            _UIDispatcher = Dispatcher;

            // タップ操作に対応する
            AssociatedObject.RightTapped += AssociatedObject_RightTapped;

            // 
            if (AssociatedObject.ContextFlyout != null)
            {
                AssociatedObject.ContextFlyout.Opening += ContextFlyout_Opening;
                AssociatedObject.ContextFlyout.Closed += ContextFlyout_Closed;
            }
            else
            {
                AssociatedObject.ObserveDependencyProperty(ListViewBase.ContextFlyoutProperty)
                    .Subscribe(_ =>
                    {
                        AssociatedObject.ContextFlyout.Opening += ContextFlyout_Opening;
                        AssociatedObject.ContextFlyout.Closed += ContextFlyout_Closed;
                    });
            }

            base.OnAttached();
        }

        private void ContextFlyout_Closed(object sender, object e)
        {
            _IsAssignedDataContextToFlyout = false;
        }

        private void AssociatedObject_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (_IsAssignedDataContextToFlyout) { return; }


            var selectedItem = FocusManager.GetFocusedElement();
            if (selectedItem is FrameworkElement fe)
            {
                var item = AssociatedObject.ItemFromContainer(fe);
                if (item != null)
                {
                    FlyoutSettingDataContext(AssociatedObject.ContextFlyout, item, AssociatedObject.DataContext);
                    e.Handled = true;
                    _IsAssignedDataContextToFlyout = true;
                }
            }

            if (e.Handled == false && e.OriginalSource is FrameworkElement sourceFe)
            {
                var dataContext = sourceFe.DataContext;
                if (dataContext != null)
                {
                    FlyoutSettingDataContext(AssociatedObject.ContextFlyout, dataContext, AssociatedObject.DataContext);
                    _IsAssignedDataContextToFlyout = true;
                }
            }
        }

        private void ContextFlyout_Opening(object sender, object e)
        {
            var selectedItem = FocusManager.GetFocusedElement();
            if (selectedItem is FrameworkElement fe)
            {
                var item = AssociatedObject.ItemFromContainer(fe);
                if (item != null)
                {
                    FlyoutSettingDataContext(AssociatedObject.ContextFlyout, item, AssociatedObject.DataContext);
                    _IsAssignedDataContextToFlyout = true;
                }
            }
        }

        protected override void OnDetaching()
        {
            if (AssociatedObject.ContextFlyout != null)
            {
                AssociatedObject.ContextFlyout.Opening -= ContextFlyout_Opening;
            }

            base.OnDetaching();
        }
       
        private static void FlyoutSettingDataContext(FlyoutBase flyoutbase, object dataContext, object parentDataContext)
        {
            if (flyoutbase is MenuFlyout menuFlyout)
            {
                foreach (var menuItem in menuFlyout.Items)
                {
                    RecurciveSettingDataContext(menuItem, dataContext, parentDataContext);
                }
            }
            else if (flyoutbase is Flyout flyout)
            {
                if (flyout.Content is FrameworkElement fe)
                {
                    fe.DataContext = dataContext;
                    fe.Tag = parentDataContext;
                }
            }
        }

        private static object GetFlyoutDataContext(FlyoutBase flyoutbase)
        {
            if (flyoutbase is MenuFlyout menuFlyout)
            {
                return menuFlyout.Items.FirstOrDefault()?.DataContext;
            }
            else
            {
                var flyout = flyoutbase as Flyout;
                if (flyout.Content is FrameworkElement)
                {
                    return (flyout.Content as FrameworkElement).DataContext;
                }
            }

            return null;
        }


        private static void RecurciveSettingDataContext(MenuFlyoutItemBase item, object dataContext, object parentDataContext)
        {
            item.DataContext = dataContext;
            item.Tag = parentDataContext;
            if (item is MenuFlyoutSubItem subItem)
            {
                foreach (var child in subItem.Items)
                {
                    RecurciveSettingDataContext(child, dataContext, parentDataContext);
                }
            }
        }
    }
}
