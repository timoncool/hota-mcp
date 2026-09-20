---
id: hota.version-scope
title: "HotA версии и границы совместимости"
game_scope: "Installed HotA 1.8.0; online HotA 1.8.1; Complete/SoD base; checked 2026-09-19"
topics: [versions, changelog, compatibility, Complete]
sources: [hota.download, hota.changelog, local.complete.readme, local.installed.build]
verification_status: "installed 1.8.0 verified by in-game UI; online 1.8.1 verified separately"
checked_at: "2026-09-19"
type: reference
layer: official
updated: "2026-09-20"
---
# HotA версии и границы совместимости


## Факты

- Официальная страница загрузки указывает HotA 1.8.1 (25.08.2026) как последнюю онлайн-версию. В архиве отдельно перечислены 1.8.0, 1.7.3, 1.7.2 и более старые сборки.
- HotA ставится поверх Shadow of Death или Complete. Ubisoft «HD Edition» и чистые Restoration of Erathia/Armageddon’s Blade не являются поддерживаемой основой.
- 1.8.0 добавила Bulwark и расширенную систему событий. Это применимо к установленной игре.
- 1.8.1 (25.08.2026) добавила/исправила ряд вещей поверх 1.8.0: Ice Formations и связанные шахты, лимит событий/Pandora на RMG до 10 000, лимит Ocean Bottles/Signs 255 и combined Pandora/local events 20 000, исправления Runes/online sync, Biarma, water-zone generation и custom template settings. Эти пункты **не следует считать доступными или исправленными в локальной 1.8.0**.
- Подтверждение установки: после перезапуска в главном меню текстовый UI-элемент ID3001 сообщил `Версия HotA: 1.8.0`.
- Подтверждённые хеши этой установки: `h3hota.exe` `5AAAB925F06CCCF23BB09814767590A95B84A557EB33D244800520BE4F1F18DE`; `HotA.dll` `0A1DAA1D8F29870B5CB72EBBA54A88C43A366473530B908BD23FAFC7968223A7`.

## Советы агенту

Для текущей установки используйте профиль 1.8.0 и не применяйте правила, помеченные `1.8.1-only`. При подключении другой копии всё равно проверяйте UI version и хеши. Bulwark и Runes доступны в 1.8.0; Bulwark campaign в 1.8.0 ещё отсутствует.
