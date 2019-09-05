﻿using NicoPlayerHohoema.Models;
using NicoPlayerHohoema.Models.Helpers;
using NicoPlayerHohoema.Services;
using NicoPlayerHohoema.Services.Helpers;
using NicoPlayerHohoema.Services.Page;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Prism.Windows.Navigation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Concurrency;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml.Navigation;

namespace NicoPlayerHohoema.Services
{
    public struct PageNavigationEventArgs
    {
        public HohoemaPageType PageType { get; set; }
        public object Paramter { get; set; }
        public bool IsMainViewTarget { get; set; }
        public bool IsForgetNavigation { get; set; }
    }

    public class PageNavigationEvenet : PubSubEvent<PageNavigationEventArgs> { }


    public class PageManager : BindableBase
    {
        public PageManager(
            INavigationService ns,
            IScheduler scheduler,
            IEventAggregator eventAggregator,
            AppearanceSettings appearanceSettings, 
            CacheSettings cacheSettings,
            HohoemaPlaylist playlist, 
            PlayerViewManager playerViewManager, 
            DialogService dialogService
            )
        {
            NavigationService = ns;
            Scheduler = scheduler;
            EventAggregator = eventAggregator;
            AppearanceSettings = appearanceSettings;
            CacheSettings = cacheSettings;
            HohoemaPlaylist = playlist;
            PlayerViewManager = playerViewManager;
            HohoemaDialogService = dialogService;
            CurrentPageType = HohoemaPageType.RankingCategoryList;


            EventAggregator.GetEvent<PageNavigationEvenet>()
                .Subscribe(args => 
                {
                    var isMainView = ApplicationView.GetForCurrentView().Id == MainViewId;
                    if (args.IsMainViewTarget && isMainView)
                    {
                        Navigation(args);
                    }
                    
                    if (!args.IsMainViewTarget && !isMainView)
                    {
                        Navigation(args);
                    }
                }
                , ThreadOption.UIThread);
        }

        void Navigation(PageNavigationEventArgs args)
        {
            var pageType = args.PageType;
            var parameter = args.Paramter;
            var isForgetNavigation = args.IsForgetNavigation;

            Scheduler.Schedule(async () =>
            {
                using (var releaser = await _NavigationLock.LockAsync())
                {
                    // メインウィンドウでウィンドウ全体で再生している場合は
                    // 強制的に小窓モードに切り替えてページを表示する
                    if (!PlayerViewManager.IsPlayerSmallWindowModeEnabled
                       && PlayerViewManager.IsPlayingWithPrimaryView)
                    {
                        PlayerViewManager.IsPlayerSmallWindowModeEnabled = true;
                    }

                    PageNavigating = true;

                    try
                    {
                        if (CurrentPageType == pageType && PageNavigationParameter == parameter)
                        {
                            return;
                        }

                        await Task.Delay(30);

                        var oldPageTitle = PageTitle;
                        PageTitle = "";
                        var oldPageType = CurrentPageType;
                        CurrentPageType = pageType;
                        var oldPageParameter = PageNavigationParameter;
                        PageNavigationParameter = parameter;

                        await Task.Delay(30);

                        if (!NavigationService.Navigate(pageType.ToString(), parameter))
                        {
                            CurrentPageType = oldPageType;
                            PageTitle = oldPageTitle;
                            PageNavigationParameter = oldPageParameter;
                        }
                        else
                        {
                            if (isForgetNavigation || IsIgnoreRecordPageType(oldPageType))
                            {
                                ForgetLastPage();
                            }
                        }

                    }
                    finally
                    {
                        PageNavigating = false;
                    }

                    _ = PlayerViewManager.ShowMainView();
                }
            });
        }

        public static int MainViewId = ApplicationView.GetApplicationViewIdForWindow(CoreApplication.MainView.CoreWindow);

        public static readonly HashSet<HohoemaPageType> IgnoreRecordNavigationStack = new HashSet<HohoemaPageType>
		{
            HohoemaPageType.Splash,
            HohoemaPageType.PrologueIntroduction,
            HohoemaPageType.EpilogueIntroduction,
        };


		public static readonly HashSet<HohoemaPageType> DontNeedMenuPageTypes = new HashSet<HohoemaPageType>
		{
            HohoemaPageType.Splash,
            HohoemaPageType.PrologueIntroduction,
            HohoemaPageType.NicoAccountIntroduction,
            HohoemaPageType.VideoCacheIntroduction,
            HohoemaPageType.EpilogueIntroduction,
        };

        public static bool IsHiddenMenuPage(HohoemaPageType pageType)
        {
            return DontNeedMenuPageTypes.Contains(pageType);
        }

		public INavigationService NavigationService { get; private set; }



        private HohoemaPageType _CurrentPageType;
		public HohoemaPageType CurrentPageType
		{
			get { return _CurrentPageType; }
			set { SetProperty(ref _CurrentPageType, value); }
		}

		private string _PageTitle;
		public string PageTitle
		{
			get { return _PageTitle; }
			set { SetProperty(ref _PageTitle, value); }
		}

        public object PageNavigationParameter { get; private set; } = -1;


		private bool _PageNavigating;
		public bool PageNavigating
		{
			get { return _PageNavigating; }
			set { SetProperty(ref _PageNavigating, value); }
		}

        public HohoemaPlaylist HohoemaPlaylist { get; private set; }
        public AppearanceSettings AppearanceSettings { get; }
        public CacheSettings CacheSettings { get; }
        public PlayerViewManager PlayerViewManager { get; }
        public IScheduler Scheduler { get; }
        public IEventAggregator EventAggregator { get; }
        public DialogService HohoemaDialogService { get; }


        private Models.Helpers.AsyncLock _NavigationLock = new Models.Helpers.AsyncLock();


        /// <summary>
        /// バックナビゲーションの暴発防止等に対応したい場合に利用します
        /// </summary>
        public bool PreventBackNavigation { get; internal set; }


        private DelegateCommand<object> _OpenPageCommand;
        public DelegateCommand<object> OpenPageCommand => _OpenPageCommand
            ?? (_OpenPageCommand = new DelegateCommand<object>(parameter =>
            {
                switch (parameter)
                {
                    case string s:
                        {
                            if (Enum.TryParse<HohoemaPageType>(s, out var pageType))
                            {
                                OpenPage(pageType);
                            }

                            break;
                        }

                    case ViewModels.MenuItemViewModel item:
                        OpenPage(item.PageType, item.Parameter);
                        break;
                    case ViewModels.PinItemViewModel pin:
                        OpenPage(pin.PageType, pin.Parameter);
                        break;
                    case Interfaces.IVideoContent videoContent:
                        OpenPage(HohoemaPageType.VideoInfomation, videoContent.Id);
                        break;
                    case Interfaces.ILiveContent liveContent:
                        OpenPage(HohoemaPageType.LiveInfomation, liveContent.Id);
                        break;
                    case Interfaces.ICommunity communityContent:
                        OpenPage(HohoemaPageType.Community, communityContent.Id);
                        break;
                    case Interfaces.IMylist mylistContent:
                        OpenPage(HohoemaPageType.Mylist, new MylistPagePayload(mylistContent.Id) { Origin = mylistContent.ToMylistOrigin() }.ToParameterString());
                        break;
                    case Interfaces.IMylistItem mylistItemContent:
                        OpenPage(HohoemaPageType.Mylist, new MylistPagePayload(mylistItemContent.Id).ToParameterString());
                        break;
                    case Interfaces.IUser user:
                        OpenPage(HohoemaPageType.UserInfo, user.Id);
                        break;
                    case Interfaces.ISearchWithtag tag:
                        this.Search(SearchPagePayloadContentHelper.CreateDefault(SearchTarget.Tag, tag.Tag));
                        break;
                    case Interfaces.ITag videoTag:
                        this.Search(SearchPagePayloadContentHelper.CreateDefault(SearchTarget.Tag, videoTag.Tag));
                        break;
                    case Interfaces.ISearchHistory history:
                        this.Search(SearchPagePayloadContentHelper.CreateDefault(history.Target, history.Keyword));
                        break;
                    case Interfaces.IChannel channel:
                        OpenPage(HohoemaPageType.ChannelVideo, channel.Id);
                        break;
                }
            }));


        private DelegateCommand<object> _OpenVideoListPageCommand;
        public DelegateCommand<object> OpenVideoListPageCommand => _OpenVideoListPageCommand
            ?? (_OpenVideoListPageCommand = new DelegateCommand<object>(parameter =>
            {
                switch (parameter)
                {
                    case string s:
                        {
                            if (Enum.TryParse<HohoemaPageType>(s, out var pageType))
                            {
                                OpenPage(pageType);
                            }

                            break;
                        }

                    case Interfaces.IVideoContent videoContent:
                        if (videoContent.ProviderType == NicoPlayerHohoema.Models.Niconico.UserType.User)
                        {
                            OpenPage(HohoemaPageType.UserVideo, videoContent.ProviderId);
                        }
                        else if (videoContent.ProviderType == NicoPlayerHohoema.Models.Niconico.UserType.Channel)
                        {
                            OpenPage(HohoemaPageType.ChannelVideo, videoContent.ProviderId);
                        }
                        break;
                    case Interfaces.ILiveContent liveContent:
                        OpenPage(HohoemaPageType.LiveInfomation, liveContent.Id);
                        break;
                    case Interfaces.ICommunity communityContent:
                        OpenPage(HohoemaPageType.CommunityVideo, communityContent.Id);
                        break;
                    case Interfaces.IMylist mylistContent:
                        OpenPage(HohoemaPageType.Mylist, new MylistPagePayload(mylistContent.Id) { Origin = mylistContent.ToMylistOrigin() }.ToParameterString());
                        break;
                    case Interfaces.IMylistItem mylistItemContent:
                        OpenPage(HohoemaPageType.Mylist, new MylistPagePayload(mylistItemContent.Id).ToParameterString());
                        break;
                    case Interfaces.IUser user:
                        OpenPage(HohoemaPageType.UserVideo, user.Id);
                        break;
                    case Interfaces.ISearchWithtag tag:
                        this.Search(SearchPagePayloadContentHelper.CreateDefault(SearchTarget.Tag, tag.Tag));
                        break;
                    case Interfaces.ISearchHistory history:
                        this.Search(SearchPagePayloadContentHelper.CreateDefault(history.Target, history.Keyword));
                        break;
                    case Interfaces.IChannel channel:
                        OpenPage(HohoemaPageType.ChannelVideo, channel.Id);
                        break;
                }
            }));



        private DelegateCommand<object> _OpenContentOwnerPageCommand;
        public DelegateCommand<object> OpenContentOwnerPageCommand => _OpenContentOwnerPageCommand
            ?? (_OpenContentOwnerPageCommand = new DelegateCommand<object>(parameter =>
            {
                switch (parameter)
                {
                    case Interfaces.IVideoContent videoContent:
                        if (videoContent.ProviderType == NicoPlayerHohoema.Models.Niconico.UserType.User)
                        {
                            OpenPage(HohoemaPageType.UserInfo, videoContent.ProviderId);
                        }
                        else if (videoContent.ProviderType == NicoPlayerHohoema.Models.Niconico.UserType.Channel)
                        {
                            OpenPage(HohoemaPageType.ChannelVideo, videoContent.ProviderId);
                        }
                        
                        break;
                    case Interfaces.ILiveContent liveContent:                        
                        if (liveContent.ProviderType == Mntone.Nico2.Live.CommunityType.Community)
                        {
                            OpenPage(HohoemaPageType.Community, liveContent.ProviderId);
                        }
                        break;
                    case Interfaces.IMylistItem mylistItemContent:
                        OpenPage(HohoemaPageType.Mylist, new MylistPagePayload(mylistItemContent.Id).ToParameterString());
                        break;
                }
            }
            , parameter => parameter is Interfaces.INiconicoContent
            ));

        public bool OpenPage(Uri uri)
		{
			var path = uri.AbsoluteUri;
			// is mylist url?
			if (path.StartsWith("http://www.nicovideo.jp/mylist/") || path.StartsWith("https://www.nicovideo.jp/mylist/"))
			{
				var mylistId = uri.AbsolutePath.Split('/').Last();
				System.Diagnostics.Debug.WriteLine($"open Mylist: {mylistId}");
				OpenPage(HohoemaPageType.Mylist, new MylistPagePayload(mylistId).ToParameterString());
				return true;
			}


			if (path.StartsWith("http://www.nicovideo.jp/watch/") || path.StartsWith("https://www.nicovideo.jp/watch/"))
			{
				// is nico video url?
				var videoId = uri.AbsolutePath.Split('/').Last();
				System.Diagnostics.Debug.WriteLine($"open Video: {videoId}");
                HohoemaPlaylist.PlayVideo(videoId);

                return true;
            }

			if (path.StartsWith("http://com.nicovideo.jp/community/") || path.StartsWith("https://com.nicovideo.jp/community/"))
			{
				var communityId = uri.AbsolutePath.Split('/').Last();
				OpenPage(HohoemaPageType.Community, communityId);

                return true;
            }

            if (path.StartsWith("http://com.nicovideo.jp/user/") || path.StartsWith("https://com.nicovideo.jp/user/"))
            {
                var userId = uri.AbsolutePath.Split('/').Last();
                OpenPage(HohoemaPageType.UserInfo, userId);

                return true;
            }

            if (path.StartsWith("http://ch.nicovideo.jp/") || path.StartsWith("https://ch.nicovideo.jp/"))
            {
                var elem = uri.AbsolutePath.Split('/');
                if (elem.Any(x => x == "article" || x == "blomaga"))
                {
                    return false;
                }
                else
                {
                    OpenPage(HohoemaPageType.ChannelVideo, elem.Last());
                }

                return true;
            }

            Debug.WriteLine($"Urlを処理できませんでした : " + uri.OriginalString);

            return false;
        }

        public void OpenDebugPage()
        {
            NavigationService.Navigate("Debug", null);
        }

		public void OpenPage(HohoemaPageType pageType, object parameter = null, bool isForgetNavigation = false)
		{
            EventAggregator.GetEvent<PageNavigationEvenet>()
                .Publish(new PageNavigationEventArgs()
                {
                    PageType = pageType,
                    Paramter = parameter,
                    IsForgetNavigation = isForgetNavigation,
                    IsMainViewTarget = true,
                });
        }


		public bool IsIgnoreRecordPageType(HohoemaPageType pageType)
		{
			return IgnoreRecordNavigationStack.Contains(pageType);
		}

		public void ForgetLastPage()
		{
			NavigationService.RemoveLastPage();
		}



		/// <summary>
		/// 外部で戻る処理が行われた際にPageManager上での整合性を取ります
		/// </summary>
		public void OnNavigated(NavigatedToEventArgs e)
		{
			if (e.NavigationMode == NavigationMode.Back || e.NavigationMode == NavigationMode.Forward)
			{
                string pageTypeString = null;

                if (e.SourcePageType.Name.EndsWith("Page"))
                {
                    pageTypeString = e.SourcePageType.Name.Remove(e.SourcePageType.Name.IndexOf("Page"));
                }
                else if (e.SourcePageType.Name.EndsWith("TV"))
                {
                    pageTypeString = e.SourcePageType.Name.Remove(e.SourcePageType.Name.IndexOf("Page_TV"));
                }
                else if (e.SourcePageType.Name.EndsWith("Mobile"))
                {
                    pageTypeString = e.SourcePageType.Name.Remove(e.SourcePageType.Name.IndexOf("Page_Mobile"));
                }


                if (pageTypeString != null)
                { 
                    HohoemaPageType pageType;
					if (Enum.TryParse(pageTypeString, out pageType))
					{
						try
						{
							PageNavigating = true;

							CurrentPageType = pageType;
						}
						finally
						{
							PageNavigating = false;
						}

						System.Diagnostics.Debug.WriteLine($"navigated : {pageType.ToString()}");
					}
					else
					{
						throw new NotSupportedException();
					}

                    PageNavigationParameter = e.Parameter;
                }
                else
				{
					throw new Exception();
				}
			}
		}

		/// <summary>
		/// 画面遷移の履歴を消去します
		/// </summary>
		/// <remarks>
		/// ログイン後にログイン画面の表示履歴を消す時や
		/// ログアウト後にログイン状態中の画面遷移を消すときに利用します。
		/// </remarks>
		public void ClearNavigateHistory()
		{
            Scheduler.Schedule(() =>
            {
                NavigationService.ClearHistory();
            });
			
		}

		public string CurrentDefaultPageTitle()
		{
			return PageTypeToTitle(CurrentPageType);
		}


        public void OpenIntroductionPage()
        {
            OpenPage(HohoemaPageType.PrologueIntroduction);
        }

        public void OpenStartupPage()
        {
            Scheduler.Schedule(() =>
            {
                if (Models.Helpers.InternetConnection.IsInternet())
                {
                    if (IsIgnoreRecordPageType(AppearanceSettings.StartupPageType))
                    {
                        AppearanceSettings.StartupPageType = HohoemaPageType.RankingCategoryList;
                    }

                    try
                    {
                        OpenPage(AppearanceSettings.StartupPageType);
                    }
                    catch
                    {
                        OpenPage(HohoemaPageType.RankingCategoryList);
                    }
                }
                else if (CacheSettings.IsUserAcceptedCache)
                {
                    OpenPage(HohoemaPageType.CacheManagement);
                }
                else
                {
                    OpenPage(HohoemaPageType.Settings);
                }
            });
        }

		public static string PageTypeToTitle(HohoemaPageType pageType)
		{
            return pageType.ToCulturelizeString();
		}


        #region Working Popup (with bloking UI)


        public async Task StartNoUIWork(string title, Func<IAsyncAction> actionFactory)
        {
            StartWork?.Invoke(title, 1);

            using (var cancelSource = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            {
                await actionFactory().AsTask(cancelSource.Token);

                ProgressWork?.Invoke(1);

                await Task.Delay(1000);

                if (cancelSource.IsCancellationRequested)
                {
                    CancelWork?.Invoke();
                }
                else
                {
                    CompleteWork?.Invoke();
                }
            }
        }
        public async Task StartNoUIWork(string title, int totalCount, Func<IAsyncActionWithProgress<uint>> actionFactory)
        {
            StartWork?.Invoke(title, (uint)totalCount);

            var progressHandler = new Progress<uint>((x) => ProgressWork?.Invoke(x));

            using (var cancelSource = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            {
                await actionFactory().AsTask(cancelSource.Token, progressHandler);

                await Task.Delay(500);

                if (cancelSource.IsCancellationRequested)
                {
                    CancelWork?.Invoke();
                }
                else
                {
                    CompleteWork?.Invoke();
                }
            }
        }

        public event StartExcludeUserInputWorkHandler StartWork;
        public event ProgressExcludeUserInputWorkHandler ProgressWork;
        public event CompleteExcludeUserInputWorkHandler CompleteWork;
        public event CancelExcludeUserInputWorkHandler CancelWork;



        #endregion




    }


    public delegate void StartExcludeUserInputWorkHandler(string title, uint totalCount);
	public delegate void ProgressExcludeUserInputWorkHandler(uint count);
	public delegate void CompleteExcludeUserInputWorkHandler();
	public delegate void CancelExcludeUserInputWorkHandler();




	public class PageInfo
	{
		public PageInfo(HohoemaPageType pageType, object parameter = null, string pageTitle = null)
		{
			PageType = pageType;
			Parameter = parameter;
			PageTitle = String.IsNullOrEmpty(pageTitle) ? PageManager.PageTypeToTitle(pageType) : pageTitle;
		}


		/// <summary>
		/// 実際にページナビゲーションが行われた場合はIsVirtualがfalse
		/// ページナビゲーションが行われていない場合はtrue（この場合、ぱんくずリストに表示することが目的）
		/// </summary>
		public bool IsVirtual { get; internal set; }


		public string PageTitle { get; set; }
		public HohoemaPageType PageType { get; set; }
		public object Parameter { get; set; }
	}
	
}
