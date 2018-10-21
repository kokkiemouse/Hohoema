﻿using Prism.Commands;
using Prism.Mvvm;
using System.Diagnostics;
using System.Windows.Input;
using Microsoft.Practices.Unity;

namespace NicoPlayerHohoema.Models
{
    public sealed class HohoemaPin : BindableBase
    {
        public HohoemaPageType PageType { get; set; }
        public string Parameter { get; set; }
        public string Label { get; set; }
        public string OverrideLabel { get; set; }

        ICommand _ChangeOverrideLabelCommand;
        public ICommand ChangeOverrideLabelCommand
        {
            get
            {
                return _ChangeOverrideLabelCommand
                    ?? (_ChangeOverrideLabelCommand = new DelegateCommand(async () => 
                    {
                        Debug.WriteLine("ChangeOverrideLabelCommand");

                        var dialogService = App.Current.Container.Resolve<Services.HohoemaDialogService>();
                        if (dialogService != null)
                        {
                            var name = OverrideLabel ?? $"{Label} ({Helpers.CulturelizeHelper.ToCulturelizeString(PageType)})";
                            var result = await dialogService.GetTextAsync(
                                $"{name} のリネーム",
                                "例）音楽のランキング（空欄にするとデフォルト名に戻せます）",
                                name,
                                (s) => true
                                );

                            OverrideLabel = string.IsNullOrEmpty(result) ? null : result;
                            RaisePropertyChanged(nameof(OverrideLabel));
                        }
                    }));
            }
        }
    }
}
