# Histórico de versões — Maid Installer

**Português** · [English](docs/en/CHANGELOG.md)

O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/). As versões seguem `MAIOR.MENOR.CORREÇÃO`.

## [1.2.0] — 2026-11-08

- **Biblioteca com 61 apps** (eram 39). Os 22 novos tiveram o ID conferido no WinGet: Zoom, Microsoft Teams, Thunderbird, GOG Galaxy,
  Battle.net, MSI Afterburner, qBittorrent, Notepad++, Bitwarden, Malwarebytes, CPU-Z, HWiNFO, CrystalDiskInfo, Rufus, Ventoy,
  K-Lite Codec Pack, GIMP, Krita, HandBrake, Paint.NET, Windows Terminal e Notion. Os ícones vêm dos sites oficiais.
- **Aviso do Windows quando a fila termina**: se o Maid Installer não estiver na frente, aparece uma notificação com quantos
  programas ficaram prontos e quantos falharam. Clicar nela abre a tela de Instalações. Pode ser desligado em
  **Configurações › Avisos e ajuda**. O PC continua nunca reiniciando sozinho.
- **Tour de boas-vindas**: na primeira abertura, cinco balões rápidos apresentam a Biblioteca, os Perfis, o Kit Pós-Formatação e a
  Maid Assistant. Dá para pular e rever em **Configurações › Avisos e ajuda**.

## [1.1.1] — 2026-11-06

- Tema **Claro**: o mascote da tela inicial não tem mais a moldura escura; as bordas da imagem esmaecem no fundo claro.
  Os temas Escuro e Preto e rosa não mudam.

## [1.1.0] — 2026-11-05

- **Inglês** no app inteiro: telas, mensagens, erros, catálogo, base de conhecimento da Maid Assistant e perfis prontos.
  O português (Brasil) continua disponível. Instalações novas abrem em inglês.
- Seleção de idioma em **Configurações › Idioma e aparência**: English, Português (Brasil) ou Automático (segue o Windows).
- Temas **Escuro** (novo padrão) e **Claro**, além do **Preto e rosa** original, que continua igual.
- Trocar idioma ou tema reabre só o app, nunca o PC, e espera instalações, downloads e cópias em andamento.
- O nome do usuário agora é opcional e vem vazio: sem nome, a Maid fala com você sem vocativo.
- A Maid Assistant entende perguntas em inglês.
- Instalador e desinstalador bilíngues (seguem o idioma do Windows) e documentação em inglês (`README.en.md`, `docs/en`).
- Teste que falha se algum texto de interface ficar sem tradução em inglês.

## [1.0.0] — 2026-11-04

Primeira versão completa.

- Interface preto e rosa com a mascote Maid, Status do Sistema com dados reais e Acesso Rápido.
- Biblioteca com 39 apps (IDs do WinGet verificados), pesquisa, filtros, ícones oficiais e status de instalação real.
- Instalação pelo WinGet com confirmação, fila (um app por vez), cancelamento, nova tentativa, logs por app e erros explicados.
- Perfis prontos (PC Gamer, Trabalho e Completo) e personalizados, Minhas Listas, e importar/exportar `.maidlist.json`.
- Kit Pós-Formatação com 10 verificações reais e ações opcionais, sempre com confirmação.
- Atualizações pelo `winget upgrade`, só com autorização, mais o histórico.
- Maid Assistant com base de conhecimento offline e IA local opcional (Qwen2.5 0.5B, llama.cpp), limitada aos fatos da base.
- Modo portátil para pendrive / Ventoy e "Criar cópia portátil".
- Versão instalável sem administrador (`Instalar.cmd`), configuração padrão em `Defaults\settings.default.json`,
  pacotes com SHA-256 e manifesto `release.json`.
- Licença de uso privado (proprietária, todos os direitos reservados) e avisos de componentes de terceiros.
