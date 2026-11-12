using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using BliBloup.Models;
using CommunityToolkit.Mvvm.ComponentModel;

public partial class Soundboard : ObservableObject
{
    private ObservableCollection<SoundItem> _sounds = new();

    [JsonInclude]
    public ObservableCollection<SoundItem> Sounds
    {
        get => _sounds;
        set => _sounds = value ?? new();
    }

    [ObservableProperty]
    [property: JsonInclude]
    private string? _name;
}