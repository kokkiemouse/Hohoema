﻿using NicoPlayerHohoema.Services;
using NicoPlayerHohoema.Services.Page;
using Prism.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NicoPlayerHohoema.Views.Subscriptions
{
    public sealed class OpenSubscriptionDestinationCommand : DelegateCommandBase
    {
        public OpenSubscriptionDestinationCommand(PageManager pageManager)
        {
            PageManager = pageManager;
        }

        public PageManager PageManager { get; }

        protected override bool CanExecute(object parameter)
        {
            return parameter is Models.Subscription.SubscriptionDestination;
        }

        protected override void Execute(object parameter)
        {
            if (parameter is Models.Subscription.SubscriptionDestination dest)
            {
                var mylistPagePayload = new MylistPagePayload(dest.PlaylistId);
                PageManager.OpenPage(HohoemaPageType.Mylist, mylistPagePayload.ToParameterString());
            }
        }
    }
}
