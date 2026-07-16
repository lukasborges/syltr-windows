# Estado de desenvolvimento

Última atualização: 16 de julho de 2026.

Este documento registra o ponto de retomada do desenvolvimento do Syltr for
Windows. A auditoria detalhada de paridade continua em
[`linux-fidelity-audit.md`](linux-fidelity-audit.md).

## Estado validado

- O aplicativo compila em Debug sem avisos ou erros.
- O aplicativo compila em Release sem avisos ou erros e o script
  `scripts/build-msix.ps1` gera um MSIX x64 não assinado.
- A suíte possui 125 testes aprovados e nenhum teste com falha ou ignorado.
- O script `scripts/run-isolation-spike.ps1` gera e abre a versão WinUI 3
  unpackaged usada para testes visuais.
- A janela **Adicionar serviço** foi validada visualmente e aprovada como janela
  secundária nativa do Windows: título e botão fechar nativos, comportamento
  modal, busca e ação `+` para serviço personalizado.
- Fechar a janela de catálogo reativa e devolve o foco à janela principal.

## Implementado até aqui

- Shell WinUI 3 com header compacto de botões sem borda, menu principal,
  navegação, modo não perturbe e controles nativos da janela.
- Barra lateral no estilo Ferdium com favicons, seleção, grupos de instâncias,
  contadores não lidos, reordenação persistida e menu de contexto completo.
- Catálogo com os 37 serviços da versão Linux, agrupado por categoria, com busca,
  ícones e suporte a múltiplas instâncias.
- Perfis WebView2 persistentes e isolados, popups OAuth/SSO no mesmo perfil,
  remoção de perfil, recuperação de processo e captura de memória.
- Links clicados com `target=_blank` abrem automaticamente no navegador padrão;
  redirects e popups OAuth/SSO permanecem no Syltr sem diálogo intermediário.
- O fluxo real Google Chat → Google → SSO corporativo e a abertura de links
  externos no navegador padrão foram validados pelo usuário.
- Permissões, downloads, favicons, contagem de não lidos, notificações web e
  notificações nativas do Windows.
- Correção ortográfica gerenciada pelos idiomas e dicionários do Windows, com
  atalho para as Configurações de idioma do sistema.
- Interface localizada em `pt-BR` e `en-US`, incluindo menu, header, catálogo,
  diálogos, permissões, estados, notificações, downloads e diagnósticos.
- Configuração JSON atômica com esquema/migração, preferências, ordem dos
  serviços, mute, desativação e user-agent opcional.
- Captura opt-in do console WebView2 via `SYLTR_DEBUG=1`, em JSONL local com
  origem sanitizada, limite por mensagem e rotação.
- Base de acessibilidade com nomes localizados, títulos semânticos, status como
  região viva, foco inicial e `Esc` no catálogo e menu contextual do rail por
  `Shift+F10`.
- Atalhos equivalentes ao Linux e atalhos adicionais convencionais do Windows.
- CI, documentação de contribuição, licença e avisos de terceiros.
- GitHub Releases definido como canal de distribuição, com geração de
  `.appinstaller`, workflow manual de candidato não assinado e política de
  privacidade. A publicação permanece bloqueada até a assinatura confiável.

## Decisões visuais aprovadas

- A experiência deve preservar a organização do Syltr Linux, usando convenções
  nativas do Windows quando a plataforma oferece um controle melhor.
- O rail segue a referência do Ferdium; o header principal segue a simplicidade
  de Epiphany/GNOME, com ícones de toolbar sem borda.
- O catálogo não usa `ComboBox`: ele é uma lista pesquisável com nome, URL,
  favicon e ação individual de adicionar.
- O catálogo é uma janela nativa, não um `ContentDialog` desenhado como janela.
- Na janela do catálogo, o `+` fica ao lado do campo de busca.

## Ponto exato para retomar

A etapa de fidelidade visual e de fluxo principal está funcional. O próximo
marco deve começar pelos itens técnicos restantes, nesta ordem:

1. Executar o roteiro manual de Narrador, alto contraste, escala e teclado em
   [`accessibility-test-plan.md`](accessibility-test-plan.md).
2. Testar quirks de user-agent e scripts específicos somente se um serviço
   apresentar falha real no Chromium/WebView2.
3. Solicitar assinatura confiável, substituir o publisher provisório pelo
   subject exato do certificado e então habilitar releases públicas assinadas.

Ao retomar, execute primeiro:

```powershell
dotnet test tests\Syltr.Tests\Syltr.Tests.csproj --no-restore
.\scripts\run-isolation-spike.ps1
```

Para habilitar a captura local do console WebView2 durante uma sessão de
diagnóstico, inicie o aplicativo com `SYLTR_DEBUG=1`. Essa opção não adiciona
comandos de engenharia ao menu do usuário.
