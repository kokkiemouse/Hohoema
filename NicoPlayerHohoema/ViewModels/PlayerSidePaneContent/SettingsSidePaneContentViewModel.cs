﻿using NicoPlayerHohoema.Models;
using Prism.Commands;
using Prism.Mvvm;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.UI;

namespace NicoPlayerHohoema.ViewModels.PlayerSidePaneContent
{
    public class SettingsSidePaneContentViewModel : SidePaneContentViewModelBase
    {
        public SettingsSidePaneContentViewModel(
            NGSettings ngSettings, 
            PlayerSettings playerSettings,
            IScheduler scheduler
            )
        {
            PlayerSettings = playerSettings;
            _scheduler = scheduler;

            // NG Comment
            NGCommentKeywordEnable = PlayerSettings.ToReactivePropertyAsSynchronized(x => x.NGCommentKeywordEnable, _scheduler)
            .AddTo(_CompositeDisposable);
            NGCommentKeywords = new ReactiveProperty<string>(_scheduler, string.Empty)
            .AddTo(_CompositeDisposable);

            NGCommentKeywordError = NGCommentKeywords
                .Select(x =>
                {
                    var keywords = x.Split('\r');
                    var invalidRegex = keywords.FirstOrDefault(keyword =>
                    {
                        Regex regex = null;
                        try
                        {
                            regex = new Regex(keyword);
                        }
                        catch { }
                        return regex == null;
                    });

                    if (invalidRegex == null)
                    {
                        return string.Empty;
                    }
                    else
                    {
                        return $"Error in \"{invalidRegex}\"";
                    }
                })
                .ToReadOnlyReactiveProperty(eventScheduler: _scheduler)
            .AddTo(_CompositeDisposable);

            NGCommentKeywords.Value = string.Join("\r", PlayerSettings.NGCommentKeywords.Select(x => x.Keyword)) + "\r";
            NGCommentKeywords.Throttle(TimeSpan.FromSeconds(3))
                .Subscribe(_ =>
                {
                    PlayerSettings.NGCommentKeywords.Clear();
                    foreach (var ngKeyword in NGCommentKeywords.Value.Split('\r'))
                    {
                        if (!string.IsNullOrWhiteSpace(ngKeyword))
                        {
                            PlayerSettings.NGCommentKeywords.Add(new NGKeyword() { Keyword = ngKeyword });
                        }
                    }
                })
                .AddTo(_CompositeDisposable);
        }

        public ReactiveProperty<bool> IsLowLatency { get; private set; }

        // NG Comments

        public ReactiveProperty<bool> NGCommentKeywordEnable { get; private set; }
        public ReactiveProperty<string> NGCommentKeywords { get; private set; }
        public ReadOnlyReactiveProperty<string> NGCommentKeywordError { get; private set; }


        public PlayerSettings PlayerSettings { get; }

        private readonly IScheduler _scheduler;
        
        protected override void OnDispose()
        {
            base.OnDispose();
        }

        private void OnRemoveNGCommentUserIdFromList(string userId)
        {
            var removeTarget = PlayerSettings.NGCommentUserIds.First(x => x.UserId == userId);
            PlayerSettings.NGCommentUserIds.Remove(removeTarget);
        }

    }


    public class ValueWithAvairability<T> : BindableBase
    {
        public ValueWithAvairability(T value, bool isAvairable = true)
        {
            Value = value;
            IsAvairable = isAvairable;
        }
        public T Value { get; set; }

        private bool _IsAvairable;
        public bool IsAvairable
        {
            get { return _IsAvairable; }
            set { SetProperty(ref _IsAvairable, value); }
        }
    }

}
