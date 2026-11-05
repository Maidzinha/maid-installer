# Histórico de versões — Maid Installer

O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/). As versões seguem `MAIOR.MENOR.CORREÇÃO`.

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
