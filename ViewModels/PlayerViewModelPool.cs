using System.Collections.Concurrent;
using StarResonance.DPS.Services;

namespace StarResonance.DPS.ViewModels;

/// <summary>
/// 一个简单的对象池，用于复用 PlayerViewModel 实例以减少GC压力。
/// </summary>
public class PlayerViewModelPool
{
    private readonly ConcurrentBag<PlayerViewModel> _pool = new();
    private readonly Func<PlayerViewModel> _viewModelFactory;

    /// <summary>
    /// 初始化对象池。
    /// </summary>
    /// <param name="viewModelFactory">一个用于创建新 PlayerViewModel 实例的工厂函数。</param>
    public PlayerViewModelPool(Func<PlayerViewModel> viewModelFactory)
    {
        _viewModelFactory = viewModelFactory;
    }

    /// <summary>
    /// 从池中获取一个 PlayerViewModel 实例。如果池为空，则创建一个新的。
    /// </summary>
    /// <returns>一个可用的 PlayerViewModel 实例。</returns>
    public PlayerViewModel Get()
    {
        if (_pool.TryTake(out var viewModel))
        {
            return viewModel;
        }
        return _viewModelFactory();
    }

    /// <summary>
    /// 将一个不再使用的 PlayerViewModel 实例归还到池中。
    /// </summary>
    /// <param name="viewModel">要归还的实例。</param>
    public void Return(PlayerViewModel viewModel)
    {
        // 在归还前重置对象状态，以便下次使用
        viewModel.Reset(); 
        _pool.Add(viewModel);
    }
}