using System.Collections.Generic;
using System.Threading.Tasks;
using MusicPlayer.Core.Models;

namespace MusicPlayer.Core.Interfaces;

public interface ITrackRepository
{
    Task<long> UpsertAsync(Track track);
    Task<Track?> GetByIdAsync(long id);
    Task<Track?> GetByPathAsync(string filePath);
    Task<IReadOnlyList<Track>> GetAllAsync();
    Task<IReadOnlyList<Track>> SearchAsync(string query);
    Task DeleteByPathAsync(string filePath);
    Task DeleteMissingAsync(IEnumerable<string> existingPaths);
}
