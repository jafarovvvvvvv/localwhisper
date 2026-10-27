using System.Text.Json;
using LocalWhisper.Services;

namespace LocalWhisper.Tests;

/// <summary>
/// Формат <c>~/.localwhisper/config.json</c>.
///
/// Здесь проверяется главное обещание переезда: файл, который лежит на диске у
/// живого пользователя, читается новым приложением без единой правки руками.
/// Ключи в нём <c>snake_case</c>, и camelCase, привычный для .NET, молча ничего
/// бы не увидел — приложение запустилось бы «как в первый раз», с чужой моделью
/// и без режимов.
/// </summary>
public class ConfigTests
{
    /// <summary>Настоящий файл, написанный питоновской версией.</summary>
    private const string LegacyJson = """
    {
      "whisper_provider": "local",
      "whisper_model": "large-v3-turbo",
      "whisper_device": "cuda",
      "whisper_compute_type": "float16",
      "llm_provider": "openai",
      "ollama_url": "http://localhost:11434",
      "ollama_model": "llama3.2",
      "openai_api_key": "",
      "openai_url": "https://api.openai.com/v1",
      "openai_model": "gpt-5.4-mini",
      "sample_rate": 16000,
      "silence_threshold": 0.003,
      "silence_duration": 2.0,
      "paste_method": "clipboard",
      "active_mode_id": "default",
      "active_mic_name": "Default Audio Input Device",
      "vocabulary": ["Anthropic", "CTranslate2"],
      "sound_enabled": true,
      "modes": [
        {
          "hotkey": "ctrl+space",
          "id": "default",
          "language": "ru",
          "name": "Voice to Text",
          "super_mode": false,
          "system_prompt": ""
        },
        {
          "hotkey": "ctrl+shift+space",
          "id": "super_mode",
          "language": "ru",
          "name": "Super Mode",
          "super_mode": true,
          "system_prompt": "Fix punctuation."
        }
      ]
    }
    """;

    [Fact]
    public void Reads_a_file_written_by_the_python_version()
    {
        var config = Config.FromJson(LegacyJson);

        Assert.Equal("local", config.WhisperProvider);
        Assert.Equal("large-v3-turbo", config.WhisperModel);
        Assert.Equal("cuda", config.WhisperDevice);
        Assert.Equal("float16", config.WhisperComputeType);
        Assert.Equal("openai", config.LlmProvider);
        Assert.Equal(16000, config.SampleRate);
        Assert.Equal(0.003, config.SilenceThreshold);
        Assert.Equal(2.0, config.SilenceDuration);
        Assert.Equal("clipboard", config.PasteMethod);
        Assert.Equal("default", config.ActiveModeId);
        Assert.True(config.SoundEnabled);
        Assert.Equal(["Anthropic", "CTranslate2"], config.Vocabulary);
    }

    [Fact]
    public void Reads_the_modes_with_their_hotkeys_and_prompts()
    {
        var config = Config.FromJson(LegacyJson);

        Assert.Equal(2, config.Modes.Count);
        var super = config.Modes[1];
        Assert.Equal("super_mode", super.Id);
        Assert.Equal("Super Mode", super.Name);
        Assert.Equal("ctrl+shift+space", super.Hotkey);
        Assert.Equal("ru", super.Language);
        Assert.True(super.SuperMode);
        Assert.Equal("Fix punctuation.", super.SystemPrompt);
    }

    [Fact]
    public void Writes_the_same_key_names_it_reads()
    {
        var json = Config.FromJson(LegacyJson).ToJson();

        foreach (var key in new[]
                 {
                     "whisper_provider", "whisper_model", "whisper_device", "whisper_compute_type",
                     "llm_provider", "ollama_url", "ollama_model", "openai_url", "openai_model",
                     "sample_rate", "silence_threshold", "silence_duration", "paste_method",
                     "active_mode_id", "active_mic_name", "vocabulary", "sound_enabled", "modes",
                 })
        {
            Assert.Contains($"\"{key}\"", json);
        }

        Assert.Contains("\"super_mode\"", json);
        Assert.Contains("\"system_prompt\"", json);
    }

    [Fact]
    public void Survives_a_full_round_trip()
    {
        var again = Config.FromJson(Config.FromJson(LegacyJson).ToJson());

        Assert.Equal("large-v3-turbo", again.WhisperModel);
        Assert.Equal(2, again.Modes.Count);
        Assert.Equal("ctrl+space", again.Modes[0].Hotkey);
        Assert.Equal(["Anthropic", "CTranslate2"], again.Vocabulary);
    }

    [Fact]
    public void Keeps_keys_it_does_not_know_about()
    {
        // Файл правят руками, и его же пишут другие версии приложения.
        // Незнакомый ключ должен пережить сохранение, а не исчезнуть молча.
        var json = Config.FromJson("""{"whisper_model":"tiny","something_new":{"a":1}}""").ToJson();
        Assert.Contains("something_new", json);
    }

    [Fact]
    public void An_empty_file_is_the_first_run_with_two_modes()
    {
        var config = Config.FromJson("{}");

        Assert.Equal(2, config.Modes.Count);
        Assert.Equal("default", config.Modes[0].Id);
        Assert.Equal("super_mode", config.Modes[1].Id);
        Assert.Equal("ctrl+alt+space", config.Modes[0].Hotkey);
    }

    [Fact]
    public void An_explicitly_empty_list_of_modes_is_respected()
    {
        // Пустой список — это решение человека, а не отсутствие настройки.
        Assert.Empty(Config.FromJson("""{"modes":[]}""").Modes);
    }

    [Fact]
    public void A_broken_file_falls_back_to_defaults_instead_of_refusing_to_start()
    {
        var config = Config.FromJson("{ this is not json");
        Assert.Equal("large-v3-turbo", config.WhisperModel);
        Assert.Equal(2, config.Modes.Count);
    }

    [Fact]
    public void Forgives_the_mistakes_of_hand_editing()
    {
        var config = Config.FromJson("""
        {
          // модель поменьше, чтобы быстрее
          "whisper_model": "tiny",
          "silence_duration": 1.5,
        }
        """);

        Assert.Equal("tiny", config.WhisperModel);
        Assert.Equal(1.5, config.SilenceDuration);
    }

    [Fact]
    public void Reads_a_number_that_was_written_as_a_string()
    {
        // Руками пишут и так — а порог тишины, съехавший к значению по
        // умолчанию, заметить почти невозможно.
        var config = Config.FromJson("""{"silence_threshold":"0.01","sample_rate":"48000"}""");
        Assert.Equal(0.01, config.SilenceThreshold);
        Assert.Equal(48000, config.SampleRate);
    }

    [Fact]
    public void Does_not_escape_the_plus_in_a_hotkey()
    {
        // System.Text.Json по умолчанию пишет «+» как +: формально верно,
        // а файл после этого не прочитать глазами.
        Assert.Contains("ctrl+shift+space", Config.FromJson(LegacyJson).ToJson());
    }

    [Fact]
    public void Never_writes_the_api_key_in_the_clear()
    {
        var config = Config.FromJson("{}");
        config.OpenaiApiKey = "sk-test-value-1234567890";

        var json = config.ToJson();
        Assert.DoesNotContain("sk-test-value", json);
        Assert.Contains("openai_api_key_enc", json);

        // И читается обратно — тем же ключом учётной записи Windows.
        var reloaded = Config.FromJson(json);
        reloaded.ResolveSecrets();
        Assert.Equal("sk-test-value-1234567890", reloaded.OpenaiApiKey);
    }

    [Fact]
    public void A_legacy_plaintext_key_keeps_working_and_gets_encrypted_on_save()
    {
        var config = Config.FromJson("""{"openai_api_key":"sk-legacy-plaintext"}""");
        config.ResolveSecrets();
        Assert.Equal("sk-legacy-plaintext", config.OpenaiApiKey);

        var json = config.ToJson();
        Assert.DoesNotContain("sk-legacy-plaintext", json);
        Assert.Contains("openai_api_key_enc", json);
    }

    [Fact]
    public void Clearing_the_key_in_the_window_really_clears_it()
    {
        // Питоновская версия оставляла старый шифрованный блоб на месте, и ключ
        // было невозможно убрать: он молча поднимался при следующем запуске.
        var withKey = Config.FromJson("{}");
        withKey.OpenaiApiKey = "sk-to-be-removed";
        var stored = Config.FromJson(withKey.ToJson());

        stored.OpenaiApiKey = "";
        var json = stored.ToJson();

        Assert.DoesNotContain("openai_api_key_enc", json);
        var reloaded = Config.FromJson(json);
        reloaded.ResolveSecrets();
        Assert.Equal("", reloaded.OpenaiApiKey);
    }

    [Fact]
    public void The_environment_variable_wins_over_the_file()
    {
        var before = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", "sk-from-environment");

            var config = Config.FromJson("""{"openai_api_key":"sk-from-file"}""");
            config.ResolveSecrets();
            Assert.Equal("sk-from-environment", config.OpenaiApiKey);

            // И не оседает в файле: ключ пришёл снаружи, он не наш, чтобы его хранить.
            var json = config.ToJson();
            Assert.DoesNotContain("sk-from-environment", json);
            Assert.DoesNotContain("openai_api_key_enc", json);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", before);
        }
    }

    [Fact]
    public void The_worker_gets_the_real_key_and_not_the_blob()
    {
        var config = Config.FromJson("{}");
        config.OpenaiApiKey = "sk-for-the-worker";

        var json = config.ToWorkerJson();
        Assert.Contains("sk-for-the-worker", json);
        Assert.DoesNotContain("openai_api_key_enc", json);

        // И это по-прежнему один объект JSON, который воркер разберёт.
        using var document = JsonDocument.Parse(json);
        Assert.Equal("sk-for-the-worker", document.RootElement.GetProperty("openai_api_key").GetString());
    }

    [Fact]
    public void The_active_mode_is_the_one_the_pill_button_starts()
    {
        var config = Config.FromJson(LegacyJson);
        Assert.Equal("default", config.ActiveMode?.Id);

        config.ActiveModeId = "super_mode";
        Assert.Equal("super_mode", config.ActiveMode?.Id);

        // Указатель на удалённый режим не должен оставлять кнопку без дела.
        config.ActiveModeId = "gone";
        Assert.Equal("default", config.ActiveMode?.Id);
    }
}
