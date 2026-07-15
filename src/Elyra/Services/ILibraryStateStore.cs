using Elyra.Models;

namespace Elyra.Services;

public interface ILibraryStateStore
{
    LibraryState? Load();
    void Save(LibraryState state);
    void Clear();
}
