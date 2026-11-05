# Manual de uso — Maid Installer ♡

*Prepare seu PC com carinho.*

O Maid Installer ajuda a deixar um PC pronto depois de formatar. Com ele você escolhe os programas, instala tudo pelas
fontes oficiais (WinGet), confere o sistema e tira dúvidas com a Maid Assistant.

**Regras que o app sempre segue:**
- Antes de instalar ou atualizar qualquer coisa, aparece a lista exata para você confirmar.
- Ele nunca atualiza programas sem sua autorização e nunca reinicia o PC.
- Ele não desativa o antivírus e não guarda senhas.

---

## 1. Início

- **Status do Sistema**: Windows, processador, placa de vídeo, memória e o uso real de cada um. A temperatura aparece quando o PC permite.
  Quando não dá para ler, aparece "n/d".
- **Atalhos**: abrem as telas principais.
- **Acesso Rápido**: seus programas favoritos. Clique para abrir. Os que ainda não estão instalados aparecem apagados.
  Para adicionar ou remover um favorito, use **Adicionar/Remover do Acesso Rápido** na Biblioteca.
- **Maid Assistant**: o painel de conversa (veja a seção 9).

## 2. Biblioteca de Apps

São 39 programas verificados, divididos em 8 categorias. Cada cartão mostra a descrição, o desenvolvedor, o preço e a licença,
o link oficial e se o programa já está instalado.

- **Pesquisar**: por nome, função ou apelido, sem se preocupar com acentos. Por exemplo, "gravar tela" encontra o OBS e o ShareX.
- **Filtros**: categoria, Instalados/Não instalados, Só gratuitos e Só com WinGet.
- **Instalar** um programa, ou marque vários e use **Instalar selecionados**.
- **Salvar como lista**: transforma os selecionados numa lista em *Minhas Listas*.
- **Consultar versão atual**: pergunta ao WinGet qual é a versão mais nova agora.
- Três programas não têm pacote no WinGet: Riot Client, NVIDIA App e AMD Adrenalin. Para eles, o app mostra só o site oficial.

### Como a instalação funciona

1. Aparece a **confirmação** com o que será instalado. Os já instalados ou sem pacote ficam separados.
2. A fila instala **um programa por vez** e mostra a etapa real (baixando, instalando).
3. O Windows pode pedir permissão de administrador (UAC). Se você recusar, aquele programa não é instalado e os outros seguem.
4. Em **Instalações** você acompanha a fila, cancela o que ainda não começou, tenta de novo e abre o **log** de cada programa.
5. Se algo falhar, a mensagem explica o motivo em português (sem internet, disco cheio, arquivo em uso…).

O app nunca atualiza um programa já instalado durante uma instalação. Para isso existe a tela Atualizações.

## 3. Perfis de Instalação

Perfis são conjuntos prontos de programas: **PC Gamer**, **PC Trabalho** e **PC Completo**. Você também pode criar os seus.

- **Instalar todos**: passa pela mesma confirmação e pela mesma fila. Os já instalados são pulados.
- Você pode editar o nome, a descrição e os programas. Tudo é salvo na hora.
- **Duplicar** e **Excluir** também estão disponíveis. Excluir pede confirmação e apaga só o perfil, nunca os programas.
- **Restaurar perfis prontos** recria os que você excluiu.

## 4. Minhas Listas

Funcionam como os perfis, mas são suas listas pessoais.

- **Exportar** gera um arquivo `.maidlist.json`, bom para guardar ou levar para outro PC.
- **Importar** só aceita programas que existem no catálogo verificado. O que não for reconhecido aparece listado e fica de fora.

## 5. Kit Pós-Formatação

São 10 cartões independentes. Cada um tem uma **verificação**, que só lê o sistema, e uma **ação** opcional, que sempre pede confirmação
e mostra o que vai mudar:

| Cartão | Verificação | Ação |
|---|---|---|
| Windows Update | Última atualização instalada | Procurar atualizações (só procura) ou abrir o Windows Update |
| WinGet | Versão instalada | Abrir o Instalador de Aplicativo na Microsoft Store |
| Componentes | Visual C++ e DirectX | Instalar os que faltam |
| Runtimes | .NET 8/10 e Java | Instalar os que faltam |
| Drivers | Dispositivos com erro e driver de vídeo | Abrir o Gerenciador de Dispositivos ou os drivers opcionais |
| Temporários | Tamanho do `%TEMP%` | Limpar só os arquivos antigos do seu usuário |
| Energia | Plano ativo | Ativar um plano que já existe |
| Inicialização | Programas que abrem com o Windows | Só consulta |
| Extensões | Se as extensões de arquivo aparecem | Mostrar ou esconder |
| Ferramentas | — | Abrir ferramentas do Windows |

## 6. Atualizações

- A tela lista o que o WinGet encontrou, com a versão instalada e a disponível.
- **Atualizar** um programa, ou marque vários e use **Atualizar selecionados**. Sempre aparece a confirmação com as versões.
- **Verificar atualizações** consulta de novo.
- O **histórico** guarda cada instalação e atualização feita, com data, versões e resultado.

## 7. Configurações

- **Você**: o nome pelo qual a Maid te chama.
- **Modo portátil e armazenamento**: onde estão seus dados e quanto espaço há livre.
  - **Criar cópia portátil…** copia o app e seus dados para um pendrive (veja [INSTALACAO.md](INSTALACAO.md)).
  - **Abrir pasta de dados**.
- **Maid Assistant (IA local)**:
  - **Baixar IA local**: 469 MB, do repositório oficial da Qwen no Hugging Face. O app confere o arquivo pelo SHA-256.
  - **Usar a IA local**: liga ou desliga.
  - **Remover modelo**, **Abrir pasta Models** e **Procurar de novo**.

## 8. Sobre

Mostra a versão, o modo de uso (instalado ou portátil), as pastas reais e botões para abrir os dados e os logs.

## 9. Maid Assistant

Pergunte em português, do seu jeito. Alguns exemplos:

- "Para que serve o 7-Zip?" · "O Discord é gratuito?" · "Quem faz o OBS?"
- "Qual a diferença entre VLC e Spotify?" · "Qual navegador escolher?"
- "Onde baixo o Steam?" · "O que é o erro 0x8A150061?" · "Como está a instalação?"

Ela responde só com informações da base do app. Quando não sabe, diz que ainda não tem aquilo cadastrado.
**Ela nunca instala, remove nem executa nada.**

Com a IA local, as explicações ficam mais naturais, mas continuam usando só os fatos da base.
Se a IA escrever algo que não está nos fatos, essa parte é descartada e a Maid usa a resposta da base.
Passe o mouse sobre uma mensagem para ver de onde ela veio.

## 10. Onde ficam as coisas

| O quê | Instalado | Portátil |
|---|---|---|
| Configurações | `%LocalAppData%\MaidInstaller\Config` | `MaidInstaller\Config` |
| Perfis / Listas | `…\Profiles`, `…\Lists` | `MaidInstaller\Profiles`, `MaidInstaller\Lists` |
| Logs (incluindo um log por instalação) | `…\Logs` | `MaidInstaller\Logs` |
| Modelo de IA | `…\Models` | `MaidInstaller\Models` |
| Histórico | `…\Data\historico.json` | `MaidInstaller\Data\historico.json` |

## 11. Problemas comuns

- **"WinGet não encontrado"**: instale o *Instalador de Aplicativo* pela Microsoft Store (Kit Pós-Formatação › WinGet).
- **Uma instalação falhou**: leia a mensagem na tela Instalações e o **Ver log**. Depois use **Tentar novamente**.
- **O instalador pediu reinício**: reinicie quando for melhor para você. O app nunca reinicia sozinho.
- **A IA não aparece como "Online"**: confira em Configurações se o modelo foi baixado e se **Usar a IA local** está marcado.
  Sem a IA, a Maid continua respondendo pela base.
- **Pendrive com pouco espaço**: o app avisa ao abrir quando há menos de 100 MB livres.
