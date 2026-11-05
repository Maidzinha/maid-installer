# Avisos de terceiros

O Maid Installer é proprietário (veja [LICENSE](LICENSE)). Ele inclui ou usa os componentes abaixo, que continuam
sob as licenças de seus autores. Este aviso acompanha os pacotes de distribuição, como essas licenças exigem.

| Componente | Autor | Licença | Onde |
|---|---|---|---|
| .NET 10 Runtime, WPF e Windows Desktop | .NET Foundation e colaboradores | MIT | Incluído nos pacotes (versão self-contained) |
| System.Management | .NET Foundation e colaboradores | MIT | Incluído |
| LLamaSharp e LLamaSharp.Backend.Cpu 0.30.0 | SciSharp STACK | MIT | Incluído |
| llama.cpp / ggml (dentro do LLamaSharp.Backend.Cpu) | Georgi Gerganov e colaboradores | MIT | Incluído |
| Qwen2.5-0.5B-Instruct (GGUF Q4_K_M) | Qwen Team, Alibaba Cloud | Apache-2.0 | **Não incluído**: baixado pelo usuário em Configurações, direto do repositório oficial `Qwen/` no Hugging Face |
| WinGet (Gerenciador de Pacotes do Windows) | Microsoft | MIT | **Não incluído**: faz parte do Windows (Instalador de Aplicativo) |

Os ícones dos aplicativos em `Assets/AppIcons` foram obtidos nos sites oficiais de cada programa (lista em
[docs/ICONES.md](docs/ICONES.md)). Os nomes e logotipos são marcas registradas de seus donos e aparecem
apenas para identificar os programas. Isso não indica parceria ou endosso.

## Texto da licença MIT

Aplica-se aos componentes marcados como MIT acima, cada um com o aviso de copyright do seu autor:

```
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

Copyright dos componentes MIT: (c) .NET Foundation and Contributors; (c) SciSharp STACK; (c) 2023-2024 The ggml authors.

## Apache-2.0 (modelo Qwen2.5)

O modelo não vem com o app. Quem baixa recebe o arquivo sob a licença Apache-2.0 do Qwen Team:
https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct-GGUF
