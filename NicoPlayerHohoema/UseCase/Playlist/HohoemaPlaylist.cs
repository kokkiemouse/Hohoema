﻿using I18NPortable;
using Mntone.Nico2;
using NicoPlayerHohoema.Database;
using NicoPlayerHohoema.Interfaces;
using NicoPlayerHohoema.Models;
using NicoPlayerHohoema.Models.Cache;
using NicoPlayerHohoema.Models.Helpers;
using NicoPlayerHohoema.Models.LocalMylist;
using NicoPlayerHohoema.Models.Provider;
using NicoPlayerHohoema.Repository;
using NicoPlayerHohoema.Repository.Playlist;
using NicoPlayerHohoema.Services.Helpers;
using NicoPlayerHohoema.Services.Player;
using NicoPlayerHohoema.UseCase.Playlist.Commands;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Prism.Unity;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Unity;
using Windows.Media;
using Windows.UI.Xaml.Data;
using AsyncLock = NicoPlayerHohoema.Models.Helpers.AsyncLock;
using NiconicoSession = NicoPlayerHohoema.Models.NiconicoSession;

namespace NicoPlayerHohoema.UseCase.Playlist
{
    public class PlayVideoRequestedEventArgs
    {
        internal PlayVideoRequestedEventArgs(IVideoContent requestItem)
        {
            RequestVideoItem = requestItem;
        }
        
        public IVideoContent RequestVideoItem { get; set; }
    }

    public class PlayLiveRequestedEventArgs
    {
        internal PlayLiveRequestedEventArgs(LiveVideoPlaylistItem requestItem)
        {
            RequestLiveItem = requestItem;
        }

        public LiveVideoPlaylistItem RequestLiveItem { get; set; }
    }

    public sealed class LocalPlaylistItemRemovedEventArgs
    {
        public string PlaylistId { get; internal set; }
        public IReadOnlyCollection<string> RemovedItems { get; internal set; }
    }

    public sealed class LocalPlaylistItemAddedEventArgs
    {
        public string PlaylistId { get; internal set; }
        public IReadOnlyCollection<string> AddedItems { get; internal set; }
    }

    public class LocalPlaylist : IPlaylist
    {
        private readonly PlaylistRepository _playlistRepository;

        internal LocalPlaylist(string id, PlaylistRepository playlistRepository)
        {
            Id = id;
            _playlistRepository = playlistRepository;
            ItemsAddCommand = new LocalPlaylistAddItemCommand(this);
            ItemsRemoveCommand = new LocalPlaylistRemoveItemCommand(this);
        }

        public LocalPlaylistAddItemCommand ItemsAddCommand { get; }
        public LocalPlaylistRemoveItemCommand ItemsRemoveCommand { get; }

        public string Id { get; }

        public string Label { get; set; }

        public int Count { get; set; }

        public int SortIndex { get; set; }


        public event EventHandler<LocalPlaylistItemRemovedEventArgs> ItemRemoved;
        public event EventHandler<LocalPlaylistItemAddedEventArgs> ItemAdded;


        public void AddPlaylistItem(IVideoContent item)
        {
            _playlistRepository.AddItem(Id, item.Id);
            ItemAdded?.Invoke(this, new LocalPlaylistItemAddedEventArgs()
            {
                PlaylistId = Id,
                AddedItems = new[] { item.Id }
            });
        }

        public void AddPlaylistItem(IEnumerable<IVideoContent> items)
        {
            var ids = items.Select(x => x.Id).ToList();
            _playlistRepository.AddItems(Id, ids);
            ItemAdded?.Invoke(this, new LocalPlaylistItemAddedEventArgs()
            {
                PlaylistId = Id,
                AddedItems = ids
            });
        }



        public IEnumerable<IVideoContent> GetPlaylistItems()
        {
            var items = _playlistRepository.GetItems(Id);
            return Database.NicoVideoDb.Get(items.Select(x => x.ContentId));
        }

        public bool RemovePlaylistItem(IVideoContent item)
        {
            var result = _playlistRepository.DeleteItem(Id, item.Id);

            if (result)
            {
                ItemRemoved?.Invoke(this, new LocalPlaylistItemRemovedEventArgs()
                {
                    PlaylistId = Id,
                    RemovedItems = new[] { item.Id }
                });
            }
            return result;
        }

        public int RemovePlaylistItems(IEnumerable<IVideoContent> items)
        {
            var ids = items.Select(x => x.Id).ToList();
            var result = _playlistRepository.DeleteItems(Id, ids);

            if (result > 0)
            {
                ItemRemoved?.Invoke(this, new LocalPlaylistItemRemovedEventArgs()
                {
                    PlaylistId = Id,
                    RemovedItems = ids
                });
            }

            return result;
        }
    }

    public static class PlaylistExtension
    {
        public static PlaylistOrigin GetOrigin(this IPlaylist playlist)
        {
            switch (playlist)
            {
                case PlaylistObservableCollection queueOrWatchAfter:
                    return PlaylistOrigin.Local;
                case LocalPlaylist local:
                    return PlaylistOrigin.Local;
                case MylistPlaylist mylist:
                    return PlaylistOrigin.Mylist;
                default:
                    throw new NotSupportedException(playlist?.GetType().Name);
            }
        }

        public static bool IsWatchAfterPlaylist(this IPlaylist localPlaylist)
        {
            return localPlaylist?.Id == HohoemaPlaylist.WatchAfterPlaylistId;
        }

        public static bool IsQueuePlaylist(this IPlaylist localPlaylist)
        {
            return localPlaylist?.Id == HohoemaPlaylist.QueuePlaylistId;
        }

        public static bool IsUniquePlaylist(this IPlaylist playlist)
        {
            return IsWatchAfterPlaylist(playlist) || IsQueuePlaylist(playlist);
        }
    }

    public class VideoContentEqualalityComperer : IEqualityComparer<IVideoContent>
    {
        public static VideoContentEqualalityComperer Default { get; } = new VideoContentEqualalityComperer();

        private VideoContentEqualalityComperer() { }

        public bool Equals(IVideoContent x, IVideoContent y)
        {
            return x.Id == y.Id;
        }

        public int GetHashCode(IVideoContent obj)
        {
            return obj.Id.GetHashCode();
        }
    }



    public sealed class VideoPlayedEventArgs
    {
        internal VideoPlayedEventArgs(string videoId, int count)
        {
            VideoId = videoId;
            Count = count;
        }

        public int Count { get; }
        public string VideoId { get; set; }
    }


    public class HohoemaPlaylist : FixPrism.BindableBase, IDisposable
    {
        // Windows10のメディアコントロールとHohoemaのプレイリスト機能を統合してサポート

        // 外部からの次送り、前送り
        // プレイリストリセットなどに対応する
        // 
        // 外部からの操作はイベントに切り出す

        // 画面の遷移自体はPageManagerに任せることにする
        // PageManagerに動画情報を渡すまでをやる


        public const string WatchAfterPlaylistId = "@view";
        public const string QueuePlaylistId = "@queue";



        static bool IsVideoId(string videoId)
        {
            return NiconicoRegex.IsVideoId(videoId) && !int.TryParse(videoId, out var _);
        }

        public event EventHandler<VideoPlayedEventArgs> VideoPlayed;


        public HohoemaPlaylist(
            IScheduler scheduler,
            IEventAggregator eventAggregator,
            PlaylistRepository playlistRepository,
            Models.Provider.NicoVideoProvider nicoVideoProvider,
            MylistRepository mylistRepository,
            PlayerSettings playerSettings
            )
        {
            _scheduler = scheduler;
            _eventAggregator = eventAggregator;
            _player = new PlaylistPlayer(this, playerSettings);
            _player.PlayRequested += OnPlayRequested;

            _player.ObserveProperty(x => x.CanGoNext).Subscribe(_ => _scheduler.Schedule(() => RaisePropertyChanged(nameof(CanGoNext)))).AddTo(_disposable);
            _player.ObserveProperty(x => x.CanGoBack).Subscribe(_ => _scheduler.Schedule(() => RaisePropertyChanged(nameof(CanGoBack)))).AddTo(_disposable);
            _player.ObserveProperty(x => x.Current).Subscribe(_ => _scheduler.Schedule(() => RaisePropertyChanged(nameof(CurrentItem)))).AddTo(_disposable);

            _playlistRepository = playlistRepository;
            _nicoVideoProvider = nicoVideoProvider;
            _mylistRepository = mylistRepository;
            _playerSettings = playerSettings;

            /*
            _ = ResolveItemsAsync(QueuePlaylist)
                .ContinueWith(prevTask =>
                {
                    var items = prevTask.Result;
                    foreach (var item in items)
                    {
                        AddQueue(item);
                    }

                    QueuePlaylist.CollectionChangedAsObservable()
                        .Throttle(TimeSpan.FromSeconds(0.25))
                        .Subscribe(args => PlaylistObservableCollectionChanged(QueuePlaylist, args))
                        .AddTo(_disposable);
                });
            */

            WatchAfterPlaylist = new PlaylistObservableCollection(WatchAfterPlaylistId, WatchAfterPlaylistId.Translate(), _scheduler);
            _ = ResolveItemsAsync(WatchAfterPlaylist)
                .ContinueWith(prevTask =>
                {
                    var items = prevTask.Result;
                    foreach (var item in items)
                    {
                        AddWatchAfterPlaylist(item);
                    }

                    WatchAfterPlaylist.CollectionChangedAsObservable()
                        .Throttle(TimeSpan.FromSeconds(0.25))
                        .Subscribe(args => PlaylistObservableCollectionChanged(WatchAfterPlaylist, args))
                        .AddTo(_disposable);
                });

            _isShuffleEnabled = playerSettings.IsShuffleEnable;
            _isReverseEnabled = playerSettings.IsReverseModeEnable;
            _isPlaylistLoopingEnabled = playerSettings.IsPlaylistLoopingEnabled;


            /*
            if (newOwner is INotifyCollectionChanged playlistNotifyCollectionChanged)
            {
                _ItemsObservaeDisposer = playlistNotifyCollectionChanged.CollectionChangedAsObservable()
                    .Subscribe(async _ =>
                    {
                        using (var releaser2 = await _PlaylistUpdateLock.LockAsync())
                        {
                                // 再生中アイテムが削除された時のプレイリストの動作

                                // 動画プレイヤーには影響を与えないこととする
                                // 連続再生動作の継続性が確保できればOK

                                SourceItems.Clear();
                            foreach (var newItem in newOwner.Select(x => new PlaylistItem()
                            {
                                ContentId = x,
                            }))
                            {
                                SourceItems.Add(newItem);
                            }

                            ResetRandmizedItems(SourceItems);

                            if (PlaylistSettings.IsShuffleEnable)
                            {
                                CurrentIndex = 0;
                            }
                            else
                            {
                                CurrentIndex = Current == null ? 0 : SourceItems.IndexOf(Current);
                            }

                            RaisePropertyChanged(nameof(CanGoBack));
                            RaisePropertyChanged(nameof(CanGoNext));
                        }
                    });
            }
            */
        }

        CompositeDisposable _disposable = new CompositeDisposable();




        private void PlaylistObservableCollectionChanged(IPlaylist playlist, NotifyCollectionChangedEventArgs args)
        {
            switch (args.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    _playlistRepository.AddItems(playlist.Id, args.NewItems.Cast<IVideoContent>().Select(x => x.Id));
                    break;
                case NotifyCollectionChangedAction.Move:
                    // TODO: 全消し＆全追加
                    System.Diagnostics.Debug.WriteLine($"[{playlist.Id}] not implement items Move action.");
                    break;
                case NotifyCollectionChangedAction.Remove:
                    _playlistRepository.DeleteItems(playlist.Id, args.OldItems.Cast<IVideoContent>().Select(x => x.Id));
                    break;
                case NotifyCollectionChangedAction.Replace:
                    System.Diagnostics.Debug.WriteLine($"[{playlist.Id}] not implement items Replace action.");
                    break;
                case NotifyCollectionChangedAction.Reset:
                    System.Diagnostics.Debug.WriteLine($"[{playlist.Id}] not implement items Reset action.");
                    break;
            }
        }


        private readonly IScheduler _scheduler;
        private readonly IEventAggregator _eventAggregator;
        private readonly PlaylistPlayer _player;
        private readonly PlaylistRepository _playlistRepository;
        private readonly NicoVideoProvider _nicoVideoProvider;
        private readonly MylistRepository _mylistRepository;
        private readonly PlayerSettings _playerSettings;

        public ReadOnlyObservableCollection<IVideoContent> QueuePlaylist => _player.QueueItems;
        public PlaylistObservableCollection WatchAfterPlaylist { get; }

        public ReadOnlyObservableCollection<IVideoContent> PlaylistItems => _player.Items;

        public IVideoContent CurrentItem
        {
            get => _player.Current;
        }

        private IPlaylist _currentPlaylist;
        public IPlaylist CurrentPlaylist
        {
            get { return _currentPlaylist; }
            private set { SetProperty(ref _currentPlaylist, value); }
        }


        private bool _isShuffleEnabled;
        public bool IsShuffleEnabled
        {
            get { return _isShuffleEnabled; }
            set
            {
                if (SetProperty(ref _isShuffleEnabled, value))
                {
                    _playerSettings.IsShuffleEnable = value;
                }
            }
        }


        private bool _isPlaylistLoopingEnabled;
        public bool IsPlaylistLoopingEnabled
        {
            get { return _isPlaylistLoopingEnabled; }
            set
            {
                if (SetProperty(ref _isPlaylistLoopingEnabled, value))
                {
                    _playerSettings.IsPlaylistLoopingEnabled = value;
                }
            }
        }


        private bool _isReverseEnabled;
        public bool IsReverseEnable
        {
            get { return _isReverseEnabled; }
            set
            {
                if (SetProperty(ref _isReverseEnabled, value))
                {
                    _playerSettings.IsReverseModeEnable = value;
                }
            }
        }


        void OnPlayRequested(object sender, IVideoContent e)
        {
            _eventAggregator.GetEvent<PlayerPlayVideoRequest>()
                .Publish(new PlayerPlayVideoRequestEventArgs() {VideoId = e.Id });

            RaisePropertyChanged(nameof(CurrentItem));
        } 



        


        public void Dispose()
        {
            _player.PlayRequested -= OnPlayRequested;
            _disposable.Dispose();
        }

        
        public void GoNext()
        {
            if (_player.CanGoNext)
            {
                _player.GoNext();
            }
        }


        public void GoBack()
        {
            if (_player.CanGoBack)
            {
                _player.GoBack();
            }
        }

        public bool CanGoNext
        {
            get => _player.CanGoNext;
        }

        public bool CanGoBack
        {
            get => _player.CanGoBack;
        }

        public async void Play(string videoId)
        {
            if (!IsVideoId(videoId))
            {
                return;
            }

            var videoContent = await ResolveVideoItemAsync(videoId);
            Play(videoContent);
        }

        public async void Play(IPlaylist playlist)
        {            
            if (playlist != CurrentPlaylist)
            {
                // PlaylistPlayerが内部的にReactiveCollectionを使っており
                // そこでAddOnSchedulerのように非同期操作を行っているため
                // _player.Itemsは同期的に扱えない
                // SetSource使用時は_player.Itemsを回避した初期アイテム設定にする
                IEnumerable<IVideoContent> items = null;
                if (playlist == QueuePlaylist)
                {
                    _player.SetSource(items = QueuePlaylist);
                }
                else if (playlist == WatchAfterPlaylist)
                {
                    _player.SetSource(items = WatchAfterPlaylist);
                }
                else
                {
                    _player.SetSource(items = await ResolveItemsAsync(playlist));
                }

                if (items?.Any() != true)
                {
                    Debug.WriteLine("Playが呼ばれたがSetSourceに指定するアイテムリストが得られなかったため再生できない。");
                    return;
                }

                CurrentPlaylist = playlist;
                
                var video = items.FirstOrDefault();

                _player.SetCurrent(video);
            }
            else
            {
                var video = _player.Items.FirstOrDefault();
                _player.SetCurrent(video);
            }


        }

        public async void Play(IVideoContent video, IPlaylist playlist = null)
        {
            if (CurrentPlaylist != playlist)
            {
                if (playlist == null)
                {
                    //_player.SetSource(null);
                }
                else if (playlist == WatchAfterPlaylist)
                {
                    _player.SetSource(WatchAfterPlaylist);
                }
                else
                {
                    _player.SetSource(await ResolveItemsAsync(playlist));
                }
            }

            CurrentPlaylist = playlist;

            _player.SetCurrent(video);
        }

     

        public void AddQueue(IVideoContent item)
        {
            _player.AddQueue(item);
        }



        public void RemoveQueue(IVideoContent item)
        {
            _player.RemoveQueue(item);
        }

        public void ClearQueue()
        {
            _player.ClearQueue();
        }




        public async void AddWatchAfterPlaylist(string videoId)
        {
            if (!IsVideoId(videoId))
            {
                return;
            }

            var videoContent = await ResolveVideoItemAsync(videoId);

            AddWatchAfterPlaylist(videoContent);
        }

        public void AddWatchAfterPlaylist(IVideoContent item)
        {
            WatchAfterPlaylist.RemoveOnScheduler(item);

            WatchAfterPlaylist.AddOnScheduler(item);
        }

        public void RemoveWatchAfter(IVideoContent item)
        {
            WatchAfterPlaylist.RemoveOnScheduler(item);
        }

        public int RemoveWatchAfterIfWatched()
        {
            var playedItems = WatchAfterPlaylist.Where(x => Database.VideoPlayedHistoryDb.IsVideoPlayed(x.Id)).ToList();
            int removeCount = 0;
            foreach (var item in playedItems)
            {
                WatchAfterPlaylist.RemoveOnScheduler(item);
                removeCount++;
            }

             _playlistRepository.DeleteItems(WatchAfterPlaylist.Id, playedItems.Select(x => x.Id));

            return removeCount;
        }


        async Task<IEnumerable<IVideoContent>> ResolveItemsAsync(IPlaylist playlist)
        {
            var playlistItems = new List<IVideoContent>();
            switch (playlist)
            {
                case LoginUserMylistPlaylist loginUserMylist:
                    var loginUserMylistResult = await loginUserMylist.GetItemsAsync(0, 50);
                    return loginUserMylistResult.Items;

                case MylistPlaylist mylist:
                    var mylistResult = await mylist.GetMylistAllItems();
                    return mylistResult.Items;
                case LocalPlaylist localPlaylist:
                    {
                        var items = _playlistRepository.GetItems(playlist.Id);
                        foreach (var item in items)
                        {
                            var videoContent = await ResolveVideoItemAsync(item.ContentId);
                            playlistItems.Add(videoContent);
                        }
                    }
                    
                    break;
                case PlaylistObservableCollection specialLocalPlaylist:
                    {
                        var items = _playlistRepository.GetItems(specialLocalPlaylist.Id);
                        foreach (var item in items)
                        {
                            var videoContent = await ResolveVideoItemAsync(item.ContentId);
                            playlistItems.Add(videoContent);
                        }
                    }

                    break;
                //case PlaylistOrigin.ChannelVideos:
                //case PlaylistOrigin.UserVideos:
                default:
                    throw new NotSupportedException(playlist.GetOrigin().ToString());
            }

            return playlistItems;
        }

        async Task<IVideoContent> ResolveVideoItemAsync(string videoId)
        {
            return await _nicoVideoProvider.GetNicoVideoInfo(videoId);
        }

        Events.VideoPlayedEvent _videoPlayedEvent;
        public void PlayDone(IVideoContent playedItem)
        {
            // キューから削除する
            _player.RemoveQueue(playedItem);

            // アイテムを視聴済みにマーク
            var history = Database.VideoPlayedHistoryDb.VideoPlayed(playedItem.Id);
            System.Diagnostics.Debug.WriteLine("視聴完了: " + playedItem.Label);

            // 視聴完了のイベントをトリガー
            VideoPlayed?.Invoke(this, new VideoPlayedEventArgs(playedItem.Id, (int)history.PlayCount));

            if (_videoPlayedEvent == null)
            {
                _videoPlayedEvent = _eventAggregator.GetEvent<Events.VideoPlayedEvent>();
            }

            _videoPlayedEvent.Publish(new Events.VideoPlayedEvent.VideoPlayedEventArgs() { ContentId = playedItem.Id });
        }


        public bool PlayDoneAndTryMoveNext()
        {
            var playedItem = _player.Current;
            var result = (_player?.CanGoNext ?? false);
            // 次送りが出来る場合は次へ
            if (result)
            {
                _player.GoNext();
            }

            Task.Delay(250).ContinueWith(_ => 
            {
                PlayDone(playedItem);
            });

            return result;
        }


        private DelegateCommand<object> _PlayCommand;
        public DelegateCommand<object> PlayCommand
        {
            get
            {
                return _PlayCommand
                    ?? (_PlayCommand = new DelegateCommand<object>(item =>
                    {
                        if (item is string contentId)
                        {
                            Play(contentId);
                        }
                        else if (item is IVideoContent playlistItem)
                        {
                            Play(playlistItem);
                        }
                    },
                    item => item is string || item is IVideoContent
                    ));
            }
        }
    }



    public sealed class PlaylistPlayer : FixPrism.BindableBase, IDisposable
    {
        // PlaylistPlayerは再生アイテムの遷移をサポートする
        // 次・前の移動はPlayRequestedイベントを通じてやり取りする

        // 実際に再生が開始された際に、PlayStartedメソッドの呼び出しが必要

        // シャッフルと通し再生に関する実装の方針
        // 内部ではシャッフル用のランダム化アイテムリストを保持して
        // 常にシャッフル/通しを切り替えられるように備えている



        public event EventHandler<IVideoContent> PlayRequested;

        Random _shuffleRandom = new Random();

        private AsyncLock _PlaylistUpdateLock = new AsyncLock();

        public HohoemaPlaylist HohoemaPlaylist { get; }
        public PlayerSettings PlayerSettings { get; private set; }

        private IVideoContent _Current;
        public IVideoContent Current
        {
            get { return _Current; }
            private set
            {
                if (SetProperty(ref _Current, value))
                {
                    RaisePropertyChanged(nameof(CanGoBack));
                    RaisePropertyChanged(nameof(CanGoNext));
                }
            }
        }

        private int CurrentIndex { get; set; }

        ReactiveCollection<IVideoContent> _queueItems = new ReactiveCollection<IVideoContent>();
        public ReadOnlyObservableCollection<IVideoContent> QueueItems { get; }



        ReactiveCollection<IVideoContent> _items = new ReactiveCollection<IVideoContent>();
        public ReadOnlyObservableCollection<IVideoContent> Items { get; }

        private List<IVideoContent> _sourceItems = new List<IVideoContent>();
        IDisposable _SettingsObserveDisposer;
        IDisposable _ItemsObservaeDisposer;
        
        public PlaylistPlayer(HohoemaPlaylist hohoemaPlaylist, PlayerSettings playerSettings)
        {
            HohoemaPlaylist = hohoemaPlaylist;
            PlayerSettings = playerSettings;

            QueueItems = new ReadOnlyObservableCollection<IVideoContent>(_queueItems);
            Items = new ReadOnlyObservableCollection<IVideoContent>(_items);

            CompositeDisposable disposables = new CompositeDisposable();

            PlayerSettings.ObserveProperty(x => x.IsReverseModeEnable)
                .Subscribe(async x => 
                {
                    using (var releaser = await _PlaylistUpdateLock.LockAsync())
                    {
                        ResetItems();
                    }
                })
                .AddTo(disposables);



            PlayerSettings.ObserveProperty(x => x.IsShuffleEnable)
                .Subscribe(async _ =>
                {
                    using (var releaser = await _PlaylistUpdateLock.LockAsync())
                    {
                        _shuffleRandom = new Random();
                        ResetItems();
                    }
                })
                .AddTo(disposables);

            _SettingsObserveDisposer = disposables;
        }


        public void Dispose()
        {
            _SettingsObserveDisposer?.Dispose();
            _SettingsObserveDisposer = null;
            _ItemsObservaeDisposer?.Dispose();
            _ItemsObservaeDisposer = null;
        }

        internal async void SetSource(IEnumerable<IVideoContent> items)
        {
            using (var releaser = await _PlaylistUpdateLock.LockAsync())
            {
                _sourceItems.Clear();
                _ItemsObservaeDisposer?.Dispose();

                if (items != null)
                {
                    _sourceItems.AddRange(items);

                    if (items is ObservableCollection<IVideoContent> collection)
                    {
                        _ItemsObservaeDisposer = collection.CollectionChangedAsObservable()
                            .Do(args =>
                            {
                                _sourceItems.Clear();
                                _sourceItems.AddRange(collection);

                                ResetItems();

                                Debug.WriteLine("Playlist Updated.");
                            })
                            .Subscribe();
                    }
                }

                ResetItems();
            }
        }


        void ResetItems()
        {
            IEnumerable<IVideoContent> currentSource = _sourceItems;
            if (PlayerSettings.IsShuffleEnable)
            {
                currentSource = currentSource.Shuffle(_shuffleRandom);
            }

            if (PlayerSettings.IsReverseModeEnable)
            {
                currentSource = currentSource.Reverse();
            }
            var items = currentSource.ToList();

            _items.ClearOnScheduler();
            _items.AddRangeOnScheduler(items);

            CurrentIndex = items.Any() ? items.IndexOf(Current) : 0;

            RaisePropertyChanged(nameof(CanGoBack));
            RaisePropertyChanged(nameof(CanGoNext));
        }


        public bool CanGoBack => __CanGoBack;

        private bool __CanGoBack
        {
            get
            {
                if (!_items.Any()) { return false; }

                if (this.PlayerSettings.IsPlaylistLoopingEnabled)
                {
                    return true;
                }
                else
                {
                    return _items.Count >= 2 && CurrentIndex >= 1;
                }
            }
        }

        
        public void GoBack()
        {
            __GoBack();
        }

        private void __GoBack()
        {
            if (!_items.Any()) { return; }

            var prevItem = default(IVideoContent);
            int prevIndex = CurrentIndex - 1;
            if (prevIndex < 0)
            {
                prevIndex = _items.Count - 1;
            }

            prevItem = _items.ElementAt(prevIndex);

            if (prevItem != null)
            {
                Current = prevItem;
                CurrentIndex = prevIndex;

                PlayRequested?.Invoke(this, prevItem);
            }
            else
            {
                throw new Exception();
            }
        }


        public bool CanGoNext
        {
            get { return __CanGoNext; }
        }

        private bool __CanGoNext
        {
            get
            {
                if (_queueItems.Any()) { return true; }
                if (!_items.Any()) { return false; }

                if (PlayerSettings.IsPlaylistLoopingEnabled)
                {
                    return true;
                }
                else
                {
                    return _items.Count >= 2 && _items.Count > (CurrentIndex + 1);
                }
            }
        }

        public void GoNext()
        {
            __GoNext();
        }

        private async void __GoNext()
        {
            using (var releaser = await _PlaylistUpdateLock.LockAsync())
            {
                if (_queueItems.Any())
                {
                    var firstQueue = _queueItems.First();
                    _queueItems.RemoveOnScheduler(firstQueue);
                    Current = firstQueue;

                    PlayRequested?.Invoke(this, firstQueue);
                }
                else 
                {
                    if (!_items.Any()) { return; }

                    var prevPlayed = Current;
                    var nextIndex = CurrentIndex + 1;

                    if (nextIndex >= _sourceItems.Count)
                    {
                        ResetItems();
                        nextIndex = 0;
                    }

                    var nextItem = _items
                        .ElementAt(nextIndex);

                    if (nextItem != null)
                    {
                        Current = nextItem;
                        CurrentIndex = nextIndex;

                        PlayRequested?.Invoke(this, nextItem);
                    }
                    else
                    {
                        throw new Exception();
                    }
                }
            }
        }

        internal async void SetCurrent(IVideoContent item)
        {
            if (item == null) { throw new Exception(); }

            using (var releaser = await _PlaylistUpdateLock.LockAsync())
            {
                // GoNext/GoBack内でCurrentが既に変更済みの場合はスキップ
                // Playlist外から直接PlaylistItemが変更された場合にのみ
                // 現在再生位置の更新を行う
                Current = item;
                if (_items.Any())
                {
                    CurrentIndex = _items.IndexOf(Current);
                }
                else
                {
                    CurrentIndex = 0;
                }

                PlayRequested?.Invoke(this, Current);
            }
        }

        internal void AddQueue(IVideoContent item)
        {
            _queueItems.RemoveOnScheduler(item);
            _queueItems.AddOnScheduler(item);

            RaisePropertyChanged(nameof(CanGoNext));
        }

        internal void RemoveQueue(IVideoContent item)
        {
            _queueItems.RemoveOnScheduler(item);
            RaisePropertyChanged(nameof(CanGoNext));
        }

        internal void ClearQueue()
        {
            _queueItems.ClearOnScheduler();
            RaisePropertyChanged(nameof(CanGoNext));
        }
    }

    public sealed class PlaylistObservableCollection : ReactiveCollection<IVideoContent>, IPlaylist
    {
        public PlaylistObservableCollection(string id, string label, IScheduler scheduler)
            :base(scheduler)
        {
            Id = id;
            Label = label;
        }

        public int SortIndex => 0;

        public string Id { get; }

        public string Label { get; }
    }
    
    public class LiveVideoPlaylistItem : Interfaces.INiconicoContent
    {
        public LiveVideoPlaylistItem(string contentId, string title)
        {
            ContentId = contentId;
            Title = title;
        }

        [DataMember]
        public string ContentId { get; }

        [DataMember]
        public string Title { get; }

        [IgnoreDataMember]
        string INiconicoObject.Id => ContentId;

        [IgnoreDataMember]
        string INiconicoObject.Label => Title;
    }

    public enum PlaylistItemType
    {
        Video,
        Live,
    }

    

    public enum PlaybackMode
    {
        Through,
        RepeatOne,
        RepeatAll,
    }
    
    
}
