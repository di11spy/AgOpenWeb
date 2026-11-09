// AgOpenWeb
// Copyright (C) 2024-2026 AgOpenWeb Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AgOpenWeb.Models.Farm;

namespace AgOpenWeb.Services.Farm;

/// <summary>The farm book on disk, under the AgOpenWeb data root. Guidance files are not this file.</summary>
public sealed class FarmStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly object _gate = new();
    private FarmBook _book;
    public string Root { get; }

    public FarmStore(string root, FarmBook book)
    {
        Root = root;
        _book = book;
    }

    public static FarmStore Open(string? root = null)
    {
        root ??= Path.Combine(AppDataRoot.Documents, "Farm");
        Directory.CreateDirectory(root);
        FarmFixture.WriteSamples(Path.Combine(root, "samples"));
        string path = Path.Combine(root, "farm.json");
        FarmBook book;
        if (File.Exists(path))
        {
            try
            {
                book = JsonSerializer.Deserialize<FarmBook>(File.ReadAllText(path), Options) ?? FarmFixture.Create(DateTime.UtcNow);
            }
            catch (JsonException)
            {
                book = FarmFixture.Create(DateTime.UtcNow);
            }
        }
        else
        {
            book = FarmFixture.Create(DateTime.UtcNow);
            File.WriteAllText(path, JsonSerializer.Serialize(book, Options));
        }
        return new FarmStore(root, book);
    }

    public string FieldsDirectory => Path.Combine(AppDataRoot.Documents, "Fields");

    public T Read<T>(Func<FarmBook, T> read)
    {
        lock (_gate) return read(_book);
    }

    public void Update(Action<FarmBook> edit)
    {
        lock (_gate)
        {
            edit(_book);
            var path = Path.Combine(Root, "farm.json");
            var json = JsonSerializer.Serialize(_book, Options);
            File.WriteAllText(path + ".tmp", json);
            File.Move(path + ".tmp", path, overwrite: true);
        }
    }
}
