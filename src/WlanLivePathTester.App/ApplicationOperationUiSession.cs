using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using WlanLivePathTester.Core.Operations;

namespace WlanLivePathTester.App;

// UI transitions occur on the owning Dispatcher. Production supplies the same
// coordinator used by route/import actions; tests may create an isolated one.
internal sealed class ApplicationOperationUiSession(
    Dispatcher dispatcher,
    ApplicationOperationCoordinator? coordinator = null)
{
    private readonly ApplicationOperationCoordinator _coordinator = coordinator ?? new();
    private readonly Dispatcher _dispatcher = dispatcher
        ?? throw new ArgumentNullException(nameof(dispatcher));
    private ApplicationOperationUiLease? _active;

    public ApplicationOperationSnapshot Snapshot => _coordinator.Snapshot;
    public bool HasActiveUiLease => _active is not null;

    public ApplicationOperationUiLease? TryBegin(
        ApplicationOperationKind kind,
        TabControl host,
        Action? requestCancellation,
        out ApplicationOperationStartStatus status)
    {
        _dispatcher.VerifyAccess();
        ArgumentNullException.ThrowIfNull(host);
        host.VerifyAccess();
        ApplicationOperationStartResult start = _coordinator.TryBegin(kind, requestCancellation);
        status = start.Status;
        if (!start.Started) return null;

        ApplicationOperationUiLease lease = new(
            this, start.Lease!, host, host.SelectedItem as TabItem);
        _active = lease;
        try
        {
            lease.LockPeerTabs();
            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    public Task<ApplicationOperationShutdownResult> RequestShutdownAsync() =>
        _coordinator.RequestShutdownAsync();

    public bool CancelShutdownRequest() => _coordinator.CancelShutdownRequest();

    public ApplicationOperationCancellationStatus RequestCancellation() =>
        _coordinator.RequestCancellation();

    internal void VerifyAccess() => _dispatcher.VerifyAccess();

    internal bool IsCurrent(ApplicationOperationUiLease lease) => ReferenceEquals(_active, lease);

    internal void Complete(ApplicationOperationUiLease lease)
    {
        _dispatcher.VerifyAccess();
        if (!ReferenceEquals(_active, lease)) return;
        // Restore before releasing the Core lease. A late cleanup must never
        // overwrite controls belonging to a newly started operation.
        try { lease.RestorePeerTabs(); }
        finally
        {
            _active = null;
            lease.CoreLease.Dispose();
        }
    }
}

internal sealed class ApplicationOperationUiLease : IDisposable
{
    private readonly ApplicationOperationUiSession _session;
    private readonly TabControl _host;
    private readonly TabItem? _ownerTab;
    private readonly Dictionary<TabItem, TabEnabledValue> _peerStates = new();
    private INotifyCollectionChanged? _collection;
    private bool _completed;

    internal ApplicationOperationUiLease(
        ApplicationOperationUiSession session,
        ApplicationOperationLease coreLease,
        TabControl host,
        TabItem? ownerTab)
    {
        _session = session;
        CoreLease = coreLease;
        _host = host;
        _ownerTab = ownerTab;
    }

    internal ApplicationOperationLease CoreLease { get; }
    public long OperationId => CoreLease.OperationId;
    public bool IsCurrent => !_completed && _session.IsCurrent(this);

    public ApplicationOperationCancellationStatus RequestCancellation() => CoreLease.RequestCancellation();

    internal void LockPeerTabs()
    {
        _session.VerifyAccess();
        if (_collection is null)
        {
            _collection = _host.Items;
            _collection.CollectionChanged += OnTabsChanged;
        }
        foreach (TabItem tab in _host.Items.OfType<TabItem>())
        {
            if (ReferenceEquals(tab, _ownerTab) || _peerStates.ContainsKey(tab)) continue;
            _peerStates.Add(tab, new TabEnabledValue(
                tab.ReadLocalValue(UIElement.IsEnabledProperty),
                BindingOperations.GetBindingBase(tab, UIElement.IsEnabledProperty)));
            // Suspend a local binding while locked so source updates cannot
            // re-enable a peer mid-operation. Restore/re-evaluate it on exit.
            tab.SetValue(UIElement.IsEnabledProperty, false);
        }
    }

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (IsCurrent) LockPeerTabs();
    }

    internal void RestorePeerTabs()
    {
        if (_collection is not null)
        {
            _collection.CollectionChanged -= OnTabsChanged;
            _collection = null;
        }
        foreach ((TabItem tab, TabEnabledValue original) in _peerStates)
        {
            if (original.Binding is not null)
            {
                BindingOperations.SetBinding(tab, UIElement.IsEnabledProperty, original.Binding);
            }
            else if (ReferenceEquals(original.LocalValue, DependencyProperty.UnsetValue))
            {
                tab.ClearValue(UIElement.IsEnabledProperty);
            }
            else
            {
                tab.SetValue(UIElement.IsEnabledProperty, original.LocalValue);
            }
        }
        _peerStates.Clear();
    }

    public void Dispose()
    {
        if (_completed) return;
        _session.VerifyAccess();
        _completed = true;
        _session.Complete(this);
    }

    private sealed record TabEnabledValue(object LocalValue, BindingBase? Binding);
}
