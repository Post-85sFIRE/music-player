using System.Threading.Tasks;
using MusicPlayer.Core.Models;

namespace MusicPlayer.Core.Interfaces;

public interface IPlaylistRepository
{
    Task<Playlist> GetOrCreateDefaultAsync();
    Task SaveAsync(Playlist playlist);
    Task<PlaylistItem?> GetItemAsync(long itemId);
    Task RemoveItemAsync(long itemId);
    Task ReorderAsync(long itemId, int order);
    Task AddFolderAsync(LibraryFolder folder);
    Task<IReadOnlyList<LibraryFolder>> GetFoldersAsync();
}
