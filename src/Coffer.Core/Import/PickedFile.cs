namespace Coffer.Core.Import;

/// <summary>
/// A file the user chose through <see cref="IFilePicker"/>. Carries the open,
/// seekable content stream plus the original file name so the caller can build a
/// <c>StatementInput</c> (inferring the format from the extension). The caller owns
/// <see cref="Content"/> and disposes it.
/// </summary>
/// <param name="Content">Seekable stream over the picked file's bytes; owned by the caller.</param>
/// <param name="FileName">Original file name including extension.</param>
public sealed record PickedFile(Stream Content, string FileName);
