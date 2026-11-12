# Hoshino Island — лицензирование

Пути компонентов ниже указаны относительно src/.

Проект Takayom1, разрабатывается с помощью Codex.
GitHub: https://github.com/Takayom1
Telegram: https://t.me/takayom1

## Наш самостоятельный код: MIT

Файл LICENSE-HOSHINO-MIT.txt применяется к оригинальному коду в перечисленных ниже файлах. Можно использовать, менять и распространять этот код, включая коммерческое использование, с сохранением уведомления MIT.

- AiChatPanel.cs
- ApprovalDialog.cs
- AssistantAccount.cs
- AssistantPreferences.cs
- AssistantSettingsPanel.cs
- AssistantTheme.cs
- AssistantVoice.cs
- WakeProfile.cs
- WakeCalibrationWindow.cs
- CodexBridge.cs
- ConversationStore.cs
- HomeTasksPanel.cs
- HoshinoCompanion.cs
- HoshinoPalette.cs
- JournalPanel.cs
- LyricsPanel.cs
- ObsidianSettingsPanel.cs
- ObsidianStore.cs
- PhraseDeck.cs
- Services/LyricsService.cs

Исходные авторские реплики в Assets/Hoshino/phrases.json также распространяются под MIT. Встроенные рисунки/векторная геометрия персонажа и материалы третьих лиц не включены в этот перечень лицензируемого кода. Права на Blue Archive, персонажа, сторонние знаки и названия этим документом не передаются. Это неофициальный фан-проект.

## Унаследованный код: D-Island License 1.0

Остальные исходные части, в том числе MainWindow, SettingsWindow и унаследованные системные сервисы с их изменениями, остаются под лицензией LICENSE.txt. Оригинальные уведомления сохранены. Новая лицензия MIT не заменяет и не отменяет её для унаследованных частей и смешанных файлов.

Based on D-Island by h7 (ibuildrigs) - https://github.com/H7gamer/d-island

Изменено Takayom1 с помощью Codex; не является официальным выпуском D-Island. Раздел 3 исходной лицензии требует видимого полного авторства и сохранения уведомлений. Поэтому текущую сборку нельзя описывать как целиком MIT или полностью свободную от ограничений исходной лицензии. Исходники доступны, но лицензия всего приложения смешанная. Для выпуска целиком на новой свободной лицензии потребуется заменить оставшуюся унаследованную реализацию или получить необходимые разрешения.

## Зависимости

Собственные условия библиотек и референсов перечислены в THIRD-PARTY-NOTICES.txt. Условия Windows, Codex и сетевых сервисов остаются отдельными. Исторические документы в репозитории не меняют эту карту лицензий.

Дополнительно MIT: RvcVoice.cs, Assets/Voice/rvc-convert.py, scripts/prepare-speech.py, scripts/build-installer.ps1 и packaging/HoshinoIsland.iss (пути относительно src либо корня проекта по расположению). Веса HoshinoOv2 не включены.

Адаптер Assets/Voice/neural-tts.py — MIT; веса Silero и HoshinoOv2 не входят в эту лицензию и не распространяются с установщиком.
