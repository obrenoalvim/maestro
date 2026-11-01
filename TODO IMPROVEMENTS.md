# TODO IMPROVEMENTS

> Last updated: 2026-11-01

## Pending Changes

### Jogos com anticheat que reclama de quem mexe no processo
- **Category:** Feature
- **Flagged by:** Dev Sr, Arquiteto
- **Source:** GameShift (`AntiCheatDetector`, fallback por IFEO), https://github.com/lhceist41/GameShift
- **What:** lista `NeverBoost` em `maestro.json` (padrão vazio) para jogos onde o Maestro não abre o processo do jogo, só faz o resto (Medal, qBittorrent, segurar o fundo).
- **Where:** `MaestroOptions.cs`, `Conductor.BoostGame`
- **Why:** Vanguard e similares podem tratar um handle aberto no jogo como suspeito. Hoje, se o Windows recusa, o Maestro desiste sozinho; o risco é o anticheat aceitar e anotar.
- **Risk:** baixo; só vale se o usuário jogar algo com anticheat de kernel.
- **Effort:** Low

### Modo jogo que fica ligado depois de sair do jogo
- **Category:** Bug
- **Flagged by:** PO
- **Source:** GameModeExecutor, lote 18 (`docs/design/18-game-gone-nobody-there.md`)
- **What:** o Windows só solta o sinal da Game Bar no próximo toque de teclado ou mouse depois que o jogo fecha. Se ninguém mexe no PC, o modo jogo fica (até 2 h medidas pelo GME): qBittorrent segurado e Medal aberto. Opção: encerrar a sessão quando nenhum processo do jogo identificado estiver vivo por 30 s, mesmo com o sinal ligado.
- **Where:** `Conductor.TickAsync`, `GameDetector.Detect`
- **Why:** downloads noturnos ficam lentos sem motivo depois de jogar e sair.
- **Risk:** jogo que reinicia o próprio processo (Forza, segundo o GME) pode gerar dois "Você abriu".
- **Effort:** Low

### Testes do maestro da sessão (Conductor)
- **Category:** Test
- **Flagged by:** QA, Arquiteto
- **Source:** GameModeExecutor, lote 9, "The engine has no tests, and the reason is structural"
- **What:** separar relógio e amostragem do `Conductor` (injetar `TimeProvider` e a fonte de amostras) para testar: espera de 10 s no fim do jogo, Medal fechando depois de 3 min, qBittorrent freado e liberado.
- **Where:** `Conductor.cs`, `Program.cs`
- **Why:** hoje só `Balancer`, leitura de processos, diário, configuração e detecção têm teste; a cola entre eles não.
- **Risk:** refatoração toca a interface pública do `Conductor`.
- **Effort:** Medium

### "Iniciar com o Windows" duplicado com o app Inicialização
- **Category:** UI-UX
- **Flagged by:** PM, UX
- **Source:** o app Inicialização do usuário (abre a inicialização em ordem), GameModeExecutor lote 19
- **What:** o usuário já controla a inicialização pelo próprio app Inicialização (ordem + `StartupApproved`). Opções: tirar o item "Iniciar com o Windows" do menu do Maestro, ou pôr o Maestro na ordem do Inicialização logo no começo (antes do Medal e dos launchers, para ele já pegar o primeiro jogo).
- **Where:** `Tray.cs`, `AppPaths.cs` (`Autostart`)
- **Why:** dois lugares para a mesma chave confundem; se o Inicialização gerencia o Maestro, o item do menu aparece desmarcado mesmo com ele abrindo no logon.
- **Risk:** nenhum técnico; é escolha de onde o usuário prefere mandar.
- **Effort:** Low
