# Estado de desenvolvimento

Última atualização: 16 de julho de 2026.

Este documento registra o ponto de retomada do desenvolvimento do Syltr for
Windows. A auditoria detalhada de paridade continua em
[`linux-fidelity-audit.md`](linux-fidelity-audit.md).

## Estado validado

- O aplicativo compila em Debug sem avisos ou erros.
- A suíte possui 104 testes aprovados e nenhum teste com falha ou ignorado.
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
- Permissões, downloads, favicons, contagem de não lidos, notificações web e
  notificações nativas do Windows.
- Configuração JSON atômica com esquema/migração, preferências, ordem dos
  serviços, mute, desativação e user-agent opcional.
- Atalhos equivalentes ao Linux e atalhos adicionais convencionais do Windows.
- CI, documentação de contribuição, licença e avisos de terceiros.

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

1. Aplicar de fato ao WebView2 os idiomas de correção ortográfica já salvos nas
   configurações, depois de validar a estratégia de dicionários do Chromium no
   Windows.
2. Mover textos de produto em português e inglês para recursos localizados do
   WinUI.
3. Implementar importação compatível do `services.json` do Linux, deixando claro
   que as contas precisarão de novo login nos perfis WebView2.
4. Validar OAuth/SSO e classificação de links externos com serviços reais.
5. Testar quirks de user-agent e scripts específicos somente nos serviços que
   apresentarem falha real no Chromium/WebView2.

Ao retomar, execute primeiro:

```powershell
dotnet test tests\Syltr.Tests\Syltr.Tests.csproj --no-restore
.\scripts\run-isolation-spike.ps1
```

Para exibir os comandos de engenharia no menu principal durante uma sessão de
diagnóstico, inicie o aplicativo com `SYLTR_DEBUG=1`.
