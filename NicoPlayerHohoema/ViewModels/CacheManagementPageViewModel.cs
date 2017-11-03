﻿using Mntone.Nico2.Videos.WatchAPI;
using NicoPlayerHohoema.Models;
using Prism.Mvvm;
using Prism.Windows.Mvvm;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Prism.Windows.Navigation;
using System.Threading;
using System.Reactive.Disposables;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Reactive.Concurrency;
using NicoPlayerHohoema.Helpers;
using Windows.UI.Xaml.Navigation;
using Prism.Commands;
using Windows.Networking.BackgroundTransfer;
using NicoPlayerHohoema.Views.Service;
using Windows.System;
using Windows.UI.Popups;
using NicoPlayerHohoema.Services;

namespace NicoPlayerHohoema.ViewModels
{
	public class CacheManagementPageViewModel : HohoemaVideoListingPageViewModelBase<CacheVideoViewModel>
	{

        public ReadOnlyReactiveProperty<bool> IsCacheUserAccepted { get; private set; }
        public ReactiveProperty<bool> IsRequireUpdateCacheSaveFolder { get; private set; }

        public ReactiveProperty<string> CacheSaveFolderPath { get; private set; }
        public DelegateCommand OpenCurrentCacheFolderCommand { get; private set; }
        public ReactiveProperty<string> CacheFolderStateDescription { get; private set; }


        public DelegateCommand ChangeCacheFolderCommand { get; private set; }
        public DelegateCommand CheckExistCacheFolderCommand { get; private set; }
        public DelegateCommand RequireEnablingCacheCommand { get; private set; }
        public DelegateCommand ReadCacheAcceptTextCommand { get; private set; }

        private DelegateCommand _ResumeCacheCommand;
        public DelegateCommand ResumeCacheCommand
        {
            get
            {
                return _ResumeCacheCommand
                    ?? (_ResumeCacheCommand = new DelegateCommand(() =>
                    {
                        // TODO: バックグラウンドダウンロードの強制更新？
                        //await _MediaManager.StartBackgroundDownload();
                    }));
            }
        }

        NiconicoMediaManager _MediaManager;

        HohoemaDialogService _HohoemaDialogService;

        public CacheManagementPageViewModel(
            HohoemaApp app, 
            PageManager pageManager,
            HohoemaDialogService dialogService
            )
			: base(app, pageManager)
		{
			_MediaManager = app.MediaManager;
            _HohoemaDialogService = dialogService;

            IsRequireUpdateCacheSaveFolder = new ReactiveProperty<bool>(false);

            IsCacheUserAccepted = HohoemaApp.UserSettings.CacheSettings.ObserveProperty(x => x.IsUserAcceptedCache)
                .ToReadOnlyReactiveProperty();

            RequireEnablingCacheCommand = new DelegateCommand(async () => 
            {
                var result = await _HohoemaDialogService.ShowAcceptCacheUsaseDialogAsync();
                if (result)
                {
                    HohoemaApp.UserSettings.CacheSettings.IsEnableCache = true;
                    HohoemaApp.UserSettings.CacheSettings.IsUserAcceptedCache = true;
                    (App.Current).Resources["IsCacheEnabled"] = true;

                    await RefreshCacheSaveFolderStatus();

                    (App.Current as App).PublishInAppNotification(
                        InAppNotificationPayload.CreateReadOnlyNotification("キャッシュの保存先フォルダを選択してください。\n保存先が選択されると利用準備が完了します。",
                        showDuration: TimeSpan.FromSeconds(30)
                        ));

                    if (await HohoemaApp.ChangeUserDataFolder())
                    {
                        await RefreshCacheSaveFolderStatus();
                        await ResetList();

                        (App.Current as App).PublishInAppNotification(
                            InAppNotificationPayload.CreateReadOnlyNotification("キャッシュの利用準備が出来ました")
                            );
                    }
                }
            });

            ReadCacheAcceptTextCommand = new DelegateCommand(async () =>
            {
                var result = await _HohoemaDialogService.ShowAcceptCacheUsaseDialogAsync(showWithoutConfirmButton:true);
            });



            CacheFolderStateDescription = new ReactiveProperty<string>("");
            CacheSaveFolderPath = new ReactiveProperty<string>("");

            OpenCurrentCacheFolderCommand = new DelegateCommand(async () =>
            {
                await RefreshCacheSaveFolderStatus();

                var folder = await HohoemaApp.GetVideoCacheFolder();
                if (folder != null)
                {
                    await Launcher.LaunchFolderAsync(folder);
                }
            });
            

            ChangeCacheFolderCommand = new DelegateCommand(async () =>
            {
                var prevPath = CacheSaveFolderPath.Value;

                if (await HohoemaApp.ChangeUserDataFolder())
                {
                    (App.Current as App).PublishInAppNotification(
                        InAppNotificationPayload.CreateReadOnlyNotification($"キャッシュの保存先を {CacheSaveFolderPath.Value} に変更しました")
                        );

                    await RefreshCacheSaveFolderStatus();
                    await ResetList();
                }
            });
        }


        #region Implement HohoemaVideListViewModelBase

        protected override async Task NavigatedToAsync(CancellationToken cancelToken, NavigatedToEventArgs e, Dictionary<string, object> viewModelState)
        {
            await RefreshCacheSaveFolderStatus();

            if (IsRequireUpdateCacheSaveFolder.Value)
            {
                (App.Current as App).PublishInAppNotification(
                    InAppNotificationPayload.CreateReadOnlyNotification("キャッシュの保存先フォルダを選択してください。\n保存先が選択されると利用準備が完了します。",
                    showDuration: TimeSpan.FromSeconds(30)
                    ));

                if (await HohoemaApp.ChangeUserDataFolder())
                {
                    await RefreshCacheSaveFolderStatus();
                    await ResetList();

                    (App.Current as App).PublishInAppNotification(
                        InAppNotificationPayload.CreateReadOnlyNotification("キャッシュの利用準備が出来ました")
                        );
                }


            }

            await base.NavigatedToAsync(cancelToken, e, viewModelState);
        }

        protected override IIncrementalSource<CacheVideoViewModel> GenerateIncrementalSource()
		{
			return new CacheVideoInfoLoadingSource(HohoemaApp, PageManager);
		}

		protected override bool CheckNeedUpdateOnNavigateTo(NavigationMode mode)
		{
			return mode == NavigationMode.New;
		}

		protected override void PostResetList()
		{
			
		}




        #endregion

        private async Task RefreshCacheSaveFolderStatus()
        {
            var cacheFolderAccessState = await HohoemaApp.GetVideoCacheFolderState();

            CacheSaveFolderPath.Value = "";
            switch (cacheFolderAccessState)
            {
                case CacheFolderAccessState.NotAccepted:
                    CacheFolderStateDescription.Value = "キャッシュ利用の同意が必要です。 「キャッシュを有効にする」ボタンを押すと同意文書が表示されます。";
                    break;
                case CacheFolderAccessState.NotEnabled:
                    CacheFolderStateDescription.Value = "キャッシュの有効化が必要です";
                    break;
                case CacheFolderAccessState.NotSelected:
                    CacheFolderStateDescription.Value = "フォルダを選択するとキャッシュ機能が使えるようになります";
                    break;
                case CacheFolderAccessState.SelectedButNotExist:
                    CacheFolderStateDescription.Value = "選択されたフォルダが確認できません。外付けストレージを再接続するか、キャッシュ先フォルダを再選択してください。";
                    CacheSaveFolderPath.Value = "?????";
                    break;
                case CacheFolderAccessState.Exist:
                    CacheFolderStateDescription.Value = "キャッシュ利用の準備ができました";
                    break;
                default:
                    break;
            }

            var folder = await HohoemaApp.GetVideoCacheFolder();
            if (folder != null)
            {
                CacheSaveFolderPath.Value = $"{folder.Path}";
            }


            IsRequireUpdateCacheSaveFolder.Value = 
                cacheFolderAccessState == CacheFolderAccessState.SelectedButNotExist
                || cacheFolderAccessState == CacheFolderAccessState.NotSelected
                ;
        }

    }


    public class CacheVideoViewModel : VideoInfoControlViewModel
	{
        public ReadOnlyReactiveProperty<DateTime> CacheRequestTime { get; private set; }

        public CacheVideoViewModel(NicoVideo nicoVideo, PageManager pageManager)
			: base(nicoVideo, pageManager, isNgEnabled:false)
		{
            CacheRequestTime = nicoVideo.ObserveProperty(x => x.CachedAt)
                .ToReadOnlyReactiveProperty()
                .AddTo(_CompositeDisposable);
		}

        protected override VideoPlayPayload MakeVideoPlayPayload()
		{
			var payload = base.MakeVideoPlayPayload();

//			payload.Quality = Quality;

			return payload;
		}
    }


	public class CacheVideoInfoLoadingSource : HohoemaIncrementalSourceBase<CacheVideoViewModel>
	{
		
		HohoemaApp _HohoemaApp;
		PageManager _PageManager;


		public List<NicoVideo> RawList { get; private set; }



        public override uint OneTimeLoadCount => (uint)10;

        public CacheVideoInfoLoadingSource(HohoemaApp app, PageManager pageManager)
            : base()
		{
			_HohoemaApp = app;
			_PageManager = pageManager;
		}

        protected override Task<IAsyncEnumerable<CacheVideoViewModel>> GetPagedItemsImpl(int head, int count)
        {
            return Task.FromResult(RawList.Skip(head).Take(count)
                .Select(x => new CacheVideoViewModel(x, _PageManager)).ToAsyncEnumerable());
        }

        protected override async Task<int> ResetSourceImpl()
        {
            // 
            var contentFinder = _HohoemaApp.ContentProvider;
            var mediaManager = _HohoemaApp.MediaManager;

            while (!mediaManager.IsInitialized)
            {
                await Task.Delay(50).ConfigureAwait(false);
            }

            var list = new List<NicoVideo>();

            foreach (var item in mediaManager.CacheVideos.ToArray())
            {
                if (item.GetDividedQualityNicoVideo(NicoVideoQuality.Smile_Low).IsCacheRequested
                    || item.GetDividedQualityNicoVideo(NicoVideoQuality.Smile_Original).IsCacheRequested)
                {
                    list.Add(item);
                }
            }

            RawList = list.OrderBy(x => x.CachedAt.Ticks).Reverse().ToList();

            return RawList.Count;
        }
    }

}
