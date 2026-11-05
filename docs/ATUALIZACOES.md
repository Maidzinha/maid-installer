# Futuras atualizações — como o projeto está preparado

Este documento é para quem for lançar uma versão nova do Maid Installer.

## Princípios

- **O app nunca se atualiza sozinho** e não procura versões novas na internet. Uma atualização é sempre o usuário trocando os
  arquivos do programa, pelo `Instalar.cmd` ou por uma nova cópia portátil. Isso segue a regra "nunca atualizar sem autorização".
- **Programa e dados ficam separados.** Os arquivos do programa podem ser trocados à vontade. Os dados do usuário (`Config`, `Profiles`,
  `Lists`, `Data`, `Models`, `Logs`, `Cache`) nunca são sobrescritos:
  - Na versão instalada, os dados ficam em `%LocalAppData%\MaidInstaller` e o programa em `%LocalAppData%\Programs\MaidInstaller`.
  - Na versão portátil, `publish-portable.ps1` e **Criar cópia portátil** só substituem os arquivos do app e mantêm as pastas de dados.

## Versões de formato (`schemaVersion`)

Todo arquivo JSON tem `schemaVersion`:

| Arquivo | Classe | Versão atual |
|---|---|---|
| `Config/settings.json` | `AppSettings` | 1 |
| `Profiles/*.json`, `Lists/*.json` | `AppCollection` | 1 |
| `*.maidlist.json` (exportação) | `CollectionFile` | 1 (versões mais novas são recusadas com mensagem) |
| `Catalog/catalog.json`, `knowledge.json`, `profiles.json` | `AppCatalog`, `KnowledgeBase`, `DefaultProfiles` | 1 (vêm com o app) |
| `release.json` (entrega) | — | 1 |

**Compatibilidade com versões mais novas.** Um pendrive pode ser usado por uma versão nova e depois por uma antiga. Por isso:
- `AppSettings` e `AppCollection` guardam os campos que não conhecem (`[JsonExtensionData] ExtraFields`) e os gravam de volta sem alteração.
- O `schemaVersion` nunca é rebaixado ao salvar.

Os testes `Settings_from_newer_version_keep_unknown_fields_and_version` e `List_from_newer_version_keeps_unknown_fields_and_version`
garantem esse comportamento.

**Mudar um formato.** Para mudar o formato das configurações:
1. Aumente `AppSettings.CurrentSchemaVersion`.
2. Acrescente o passo em `SettingsService.Migrate`, por exemplo `if (s.SchemaVersion == 1) { …; s.SchemaVersion = 2; }`.
3. Crie um teste com um arquivo da versão antiga.

Nunca remova um campo sem migrar o valor antigo.

## Lançar uma versão

1. Atualize `<Version>` em `src/MaidInstaller.App/MaidInstaller.App.csproj`. A tela Sobre e o `release.json` leem esse valor.
2. Acrescente a seção da versão no `CHANGELOG.md`.
3. Se o catálogo mudou: confira cada ID novo com `winget show --id <id> -e` e acrescente o ícone oficial em
   `Assets/AppIcons/<id>.png` (veja [ICONES.md](ICONES.md)).
4. Rode `build-release.ps1`. Ele roda os testes e gera os `.zip`, o `SHA256SUMS.txt` e o `release.json` em `dist\release`.
5. Teste os dois pacotes: abra o portátil num pendrive e rode `Instalar.cmd` por cima da versão anterior. Confira que listas e perfis continuam lá.

## Limitação conhecida

As listas e os perfis guardam ids do catálogo. Uma versão **antiga** que abra uma lista com um app que só existe no catálogo
**novo** ignora esse app na tela e avisa no log. Se a lista for editada nessa versão antiga, o app desconhecido sai da lista.
