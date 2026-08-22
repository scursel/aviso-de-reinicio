# Aviso de Reinício

> Lembrete diário de reinício para computadores de caixa (PDV) no Windows.
> **Desenvolvido por Scursel** — projeto open source sob a licença MIT.

O **Aviso de Reinício** fica na bandeja do sistema e, todos os dias no horário
configurado (padrão **02:00**), abre um pop-up pedindo para reiniciar o
computador. Se o funcionário estiver atendendo, ele clica em **"OK, adiar"** e
o aviso volta depois de alguns minutos — repetindo **até o computador ser
reiniciado**. Depois do reinício, o programa para de incomodar pelas
próximas 20 horas (configurável).

[![Licença MIT](https://img.shields.io/badge/Licença-MIT-blue.svg)](LICENSE)
[![Release](https://img.shields.io/github/v/release/scursel/aviso-de-reinicio)](https://github.com/scursel/aviso-de-reinicio/releases)
[![Downloads](https://img.shields.io/github/downloads/scursel/aviso-de-reinicio/total)](https://github.com/scursel/aviso-de-reinicio/releases)

Não precisa de administrador, não precisa instalar nada: é um único `.exe`
que usa o .NET Framework que já vem no Windows 10/11.

---

## Funcionalidades

- ✅ **Dois modos de aviso** (configurável na tela de configurações):
  - **Horário fixo** (padrão): avisa todo dia às HH:MM (padrão 02:00), desde que o PC
    esteja ligado há pelo menos X horas naquele momento (padrão 20 h). Avaliado
    **somente no horário do dia** — nunca dispara fora dele por causa do uptime.
  - **Ciclo por uptime**: avisa quando o PC passar de N horas ligado (padrão 24),
    em qualquer horário, repetindo até o reinício.
- ✅ Botão **"OK, adiar"** → o aviso volta após X minutos (padrão 5, configurável 1–120) e **não desaparece mais**: uma vez iniciado, o ciclo só termina com um reinício real
- ✅ O aviso **não tem X para fechar**; se ninguém clicar em 15 min (configurável), adia sozinho e volta depois. Alt+F4/Gerenciador de Tarefas registra "Adiado (janela fechada)" e o aviso volta do mesmo jeito
- ✅ **"Reiniciar agora"** reinicia o Windows em 10 segundos
- ✅ Tela de configurações com modo, horário, adiamento, reinício forçado opcional e início automático
- ✅ **Reinício forçado opcional**: após N adiamentos no mesmo ciclo, abre contagem de 60 s e reinicia sozinho
- ✅ **Log completo** (CSV, abre no Excel): cada pop-up, cada "OK", o pedido de reinício e o horário exato em que o PC voltou
- ✅ Se o app estava fechado/suspenso na hora do aviso elegível, faz catch-up ~45 s depois de voltar
- ✅ Sempre por cima das outras janelas (inclusive do PDV) + som de alerta; tenta o foco **uma vez** ao abrir e nunca rouba o foco do operador depois
- ✅ Isenção por máquina via arquivo `DesativarAviso.txt`
- ✅ Rede de segurança: tarefa agendada relança o programa no logon e a cada 10 min se ele for fechado — e o ciclo de aviso é **reconstruído** ao relançar (só um boot novo encerra um ciclo)
- ✅ **Senha de supervisor (opt-in, desligada por padrão)**: protege abrir configurações, Sair e Arquivar log
- ✅ **Atualização pelo GitHub (padrão: instalar)**: checa 1×/dia a última release; menu e balão na bandeja. Com o padrão (`AutoUpdate=1`), instala sozinho só logo após um boot recente (30 min); desligado, só avisa

## Capturas de tela

**Pop-up diário** — fica sempre por cima das outras janelas e não tem X para fechar:

![Pop-up do Aviso de Reinício](docs/screenshots/popup.png)

**Tela de configurações** — horário, adiamento, reinício forçado, estatísticas e log:

![Tela de configurações](docs/screenshots/configuracoes.png)

## Instalação

1. Baixe o instalador (`Instalador-AvisoDeReinicio-*.exe`) na página de
   [Releases](https://github.com/scursel/aviso-de-reinicio/releases) (ou compile
   o seu, veja abaixo).
2. Execute o instalador (não precisa de administrador).
3. Marque "Iniciar automaticamente com o Windows" (recomendado).
4. Pronto: o ícone azul aparece na bandeja, ao lado do relógio.

Também dá para usar o `AvisoDeReinicio.exe` direto (portátil): dois cliques e
ele já funciona, sem instalar nada.

## Uso

- **Configurar**: clique com o botão direito no ícone azul da bandeja →
  "Abrir configurações" (ou clique duas vezes no ícone).
- **Testar**: menu do ícone → "Testar pop-up agora".
- **Ver o log**: tela de configurações (tabela + estatísticas) ou o arquivo
  `%APPDATA%\AvisoDeReinicio\log.csv`.

### Regras do lembrete

**Modo "horário fixo"** (padrão, `RestartMode=fixo`): a elegibilidade é avaliada
**no horário do dia** (`RestartTime`, padrão 02:00): o pop-up aparece se, naquele
instante, o PC está ligado há ≥ `SatisfiedHours` (padrão 20 h). Exemplos com
02:00/20 h:

| Situação | Comportamento |
|---|---|
| Às 02:00 o PC está ligado há 20 h ou mais | Pop-up aparece às 02:00 |
| Boot às 10:00 (16 h às 02:00 seguinte) | **Nada** ao completar 20 h (às 06:00); pop-up só no **próximo** 02:00 (uptime 40 h) — sem deriva de horário |
| Boot depois do slot (PC desligado à noite) | Sem catch-up naquele dia; máquina que desliga/liga diariamente não é incomodada |
| App fechado/suspenso no slot elegível | Catch-up ~45 s depois de voltar |
| Reinício às 02:10 | Próximo aviso às **02:00** do dia seguinte (uptime 23 h 50 ≥ 20 h) |

**Modo "ciclo por uptime"** (`RestartMode=uptime`): o pop-up aparece quando o PC
passa de `UptimeHours` ligado (padrão 24), em qualquer horário, e volta a cada
adiamento até um reinício real. Quem reinicia no primeiro aviso fica ancorado no
mesmo horário todos os dias (24 h depois do boot). Suspensão conta como tempo
ligado; mudança do relógio não afeta este modo.

Em ambos os modos: uma vez aberto o primeiro pop-up, o ciclo só termina com um
boot novo — meia-noite, crash, saída ou relançamento do app não o cancelam (o
estado é reconstruído a partir do uptime). "OK, adiar" volta após X minutos
(configurável). Sem clique por `PopupTimeoutMinutes`, adia sozinho. Alt+F4
registra "Adiado (janela fechada)" e agenda o retorno normalmente.

| Demais situações | Comportamento |
|---|---|
| Funcionário clica "Reiniciar agora" | Reinicia em 10 s e registra no log |
| N adiamentos no mesmo ciclo (se "forçar" estiver ligado) | Contagem de 60 s e reinício automático |
| Pop-up de teste ("Testar agora" / `--demo`) | Isolado: não inicia ciclo, não loga eventos de produção e não conta para a força |

### Log (simples, em português)

O log fica em `%APPDATA%\AvisoDeReinicio\log.csv` (abre direto no Excel).
**Arquivar log** renomeia o arquivo para `log-AAAAMMDD.csv` e começa um
novo, em vez de apagar o histórico. Acima de ~2 MB o programa arquiva
sozinho. Só registra o que interessa, com nomes em português:

| DataHora | Evento | Detalhe |
|---|---|---|
| 15/08/2026 02:00 | Aviso exibido | 1º aviso do dia |
| 15/08/2026 02:01 | Adiado (OK) | próximo aviso em 5 min |
| 15/08/2026 02:06 | Reinício solicitado | pelo operador |
| 15/08/2026 02:14 | Computador reiniciado | pelo app (ou "por fora", se não foi este programa) |

Possíveis eventos: **Aviso exibido**, **Adiado (OK)**, **Adiado (automático)**,
**Adiado (janela fechada)**, **Reinício solicitado**, **Falha ao reiniciar**,
**Computador reiniciado**, **Contagem regressiva**, **Configurações alteradas**,
**Avisos desativados**, **Avisos reativados**, **Log arquivado**,
**Senha incorreta**, **Atualização disponível**, **Atualização aplicada**.
Problemas técnicos (se houver) ficam num arquivo separado, `erros.log`.

### Senha de supervisor (opt-in)

Desligada por padrão: quem não configurar não vê diferença nenhuma. Na tela
de configurações, **Definir senha de supervisor…** passa a pedir senha para
abrir as configurações, para **Sair** e para **Arquivar log**.

**Limite honesto:** sem administrador nenhuma proteção é real. O operador
pode editar `%APPDATA%\AvisoDeReinicio\config.ini` no Bloco de Notas (apagar
`SenhaHash`), matar o processo no Gerenciador de Tarefas, ou remover a chave
`Run`. A senha é barreira de conveniência, não controle de segurança.

### Máquina isenta

Crie um arquivo vazio `DesativarAviso.txt` em `%APPDATA%\AvisoDeReinicio`.
O programa continua rodando (e logando), mas não mostra pop-ups.
Apague o arquivo para voltar a avisar — vale no próximo ciclo (até 15 s),
sem precisar reiniciar o programa.

### Desinstalar

Painel de Controle → Programas → "Aviso de Reinício" → Desinstalar.
(Remove o programa, o início automático, a tarefa agendada e os atalhos.
Os logs ficam em `%APPDATA%\AvisoDeReinicio` — apague a pasta se quiser
remover tudo.)

## Compilar do código-fonte

Requisitos: Windows 10/11 (nada mais — usa o compilador `csc.exe` que já vem
no sistema). Para gerar o instalador: [Inno Setup](https://jrsoftware.org) 6.7+.

```
:: gera o AvisoDeReinicio.exe
build.bat

:: gera o instalador (saida\Instalador-*.exe)
ISCC.exe instalador.iss

:: release completo: le a versao do assembly, injeta no instalador.iss,
:: compila, roda o Inno, grava saida\SHA256SUMS.txt e cria a tag
powershell -NoProfile -ExecutionPolicy Bypass -File .\release.ps1
```

A versão do programa vive só em `[assembly: AssemblyVersion]` no
`AvisoDeReinicio.cs` (hoje **1.5.0**). `release.ps1` copia esse número para
o instalador. Use `-SkipTag` ou `-SkipInno` para testar sem tag/Inno.

### Atualização automática

Uma vez por dia o programa consulta
`https://github.com/scursel/aviso-de-reinicio/releases/latest` (redirect 302,
sem a API do GitHub). Só oferece update se a tag for **maior** que a versão
instalada — um rollback no GitHub não rebaixa o parque. O instalador é
conferido contra o `SHA256SUMS.txt` do mesmo release.

Padrão: **instalar sozinho** (balão + item de menu avisam quando há novidade;
a instalação só roda nos 30 minutos depois de um boot recente, nunca no meio
do expediente). Para só ser avisado, desmarque a opção na tela de
configurações ou grave `AutoUpdate=0` no `config.ini`. Máquinas antigas cujo
`config.ini` já traz `AutoUpdate=0` gravado mantêm a escolha explícita.

### Empurrar update por comando

Para atualizar uma máquina na hora (sem esperar o próximo boot), rode:

```
powershell -NoProfile -ExecutionPolicy Bypass -File .\atualizar-maquinas.ps1
```

O script baixa o instalador da release do GitHub, confere o SHA256 contra o
`SHA256SUMS.txt` do mesmo release, fecha o app, instala em modo silencioso e
confirma a versão gravada no disco — falhando alto se algo não bater. Não
precisa de administrador; a exceção é a instância em execução estar elevada,
caso em que o script avisa (encerre o app pelo ícone da bandeira e rode de
novo, ou use um prompt de admin). É **idempotente**: em máquina já na versão
alvo apenas informa "nada a fazer" — pode rodar no parque inteiro sem separar
as máquinas. Na próxima release, normalmente só o `-Tag` muda (o hash é lido
do próprio release):

```
powershell -NoProfile -ExecutionPolicy Bypass -File .\atualizar-maquinas.ps1 -Tag v1.6.0
```

Sem baixar o repositório, direto do **prompt do PowerShell** da máquina:

```powershell
$d = Join-Path $env:TEMP 'atualizar-aviso.ps1'; [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; (New-Object Net.WebClient).DownloadFile('https://raw.githubusercontent.com/scursel/aviso-de-reinicio/main/atualizar-maquinas.ps1', $d); powershell -NoProfile -ExecutionPolicy Bypass -File $d
```

No prompt do **CMD**, use a mesma sequência embrulhada num `-Command` com aspas
duplas (o CMD não expande `$d`, ao contrário do PowerShell):

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; $d=Join-Path $env:TEMP 'atualizar-aviso.ps1'; (New-Object Net.WebClient).DownloadFile('https://raw.githubusercontent.com/scursel/aviso-de-reinicio/main/atualizar-maquinas.ps1',$d); powershell -NoProfile -ExecutionPolicy Bypass -File $d"
```

Máquinas na v1.0.2 **não** se auto-atualizam — precisam de um empurrão
manual até pelo menos a v1.3.0 (quando a versão passou a existir no exe).

Em desligamento híbrido (Fast Startup) o uptime não zera, então
"desligar e ligar" não conta como reinício. Para o propósito do aviso isso
está certo.

A distribuição **não é assinada** hoje: o SmartScreen do Windows trata o
instalador como desconhecido e pode avisar na primeira execução. Para
assinar depois, passe `-PfxPath` (e `-PfxPassword`) ao `release.ps1` — o
script chama o `signtool` só quando o certificado está preenchido.

O `SHA256SUMS.txt` do release prova que o arquivo baixado é o que o release
declara. **Não** prova que o release é legítimo. Sem assinatura Authenticode,
a segurança do parque = segurança da conta GitHub (o updater baixa e executa
o instalador publicado nessa conta).

Flags de desenvolvimento: `AvisoDeReinicio.exe --demo` (abre o pop-up de teste
sozinho — isolado, sem efeitos no agendador), `--config` (abre direto a tela de
configurações), `--appdir <caminho>` (usa outro diretório de dados e outro
mutex, para testar uma instância ao lado da real) e `--selftest` (roda os testes
determinísticos do agendador em `%TEMP%\AvisoDeReinicioSelftest`, sem tocar no
seu config/log; sai com código 0=ok / 1=falha).

## Estrutura do repositório

```
AvisoDeReinicio.cs   -> código-fonte completo (C# / WinForms, .NET Framework 4.x)
build.bat            -> compila o .exe com o csc.exe do Windows
release.ps1          -> monta o release (versao, instalador, SHA256, tag)
instalador.iss       -> script do instalador (Inno Setup, pt-BR, sem admin)
make-icon.ps1        -> gera o app.ico (círculo azul com seta de reinício)
registrar-tarefa.ps1 -> cria a tarefa agendada de backup no logon
atualizar-maquinas.ps1 -> empurra um update para a máquina local (baixa, confere e instala)
app.ico              -> ícone do programa
```

## Licença

[MIT](LICENSE) — use, modifique e distribua livremente.

---

*Desenvolvido por Scursel.*
