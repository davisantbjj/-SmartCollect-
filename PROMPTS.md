# Repositório de Prompts e Rastreabilidade de IA (SmartCollect)

Este documento registra o histórico dos principais prompts utilizados pela nossa equipe (Squad 03 - Atos Capital) ao longo do desenvolvimento do projeto **SmartCollect**, demonstrando a evolução da nossa engenharia de prompt, desde comandos mais genéricos até interações altamente contextualizadas.

## 1. Concepção e Regras de Negócio (Backend - C#)

### 1.1 Modelagem da Regra de Classificação de Pagamentos e Status Dinâmicos
- **Data Aproximada:** Início do Sprint de Backend
- **Objetivo:** Estruturar no Backend a lógica condicional que avalia o histórico de notificações e as datas de vencimento no momento da baixa bancária.
- **Prompt Utilizado:**
  > "Atue como um Arquiteto de Software Especialista em .NET 8 e C#. Preciso implementar uma regra de negócio financeira para o processamento de baixa de títulos. Com base nas três condições abaixo, gere um método em C# que receba a data de vencimento, data de pagamento e um booleano indicando se houve disparos de notificação anteriores. O método deve retornar um Enum (StatusResolucao):
  > 1. Se pago até o vencimento e houve notificações prévias: classifique como Prevenido. 
  > 2. Se pago após o vencimento e houve notificações de atraso: classifique como Recuperado. 
  > 3. Se pago antes de qualquer notificação do sistema: classifique como Organico.
  > Otimize o código para que ele rode dentro de um Worker assíncrono que consome dados de uma API JSON diária."
- **Resultado/Aprendizado:** A IA gerou a estrutura do Enum e a condicional de forma limpa, acelerando a transcrição da regra de negócio da Atos Capital para código.

### 1.2 Gateway de E-mail Multi-Tenant (White-label)
- **Data Aproximada:** Sprint de Mensageria e Configurações
- **Objetivo:** Criar classe de serviço para injeção dinâmica de credenciais SMTP.
- **Prompt Utilizado:**
  > "Crie uma classe de serviço em C# utilizando a biblioteca MailKit que permita injeção dinâmica de credenciais SMTP (Host, Porta, Usuário, Senha). Essa configuração é crucial para um sistema multi-tenant onde cada cliente dispara notificações usando seu e-mail corporativo institucional próprio. O método de envio deve aceitar anexos em PDF (boletos) e substituir variáveis dinâmicas como {{NomeCliente}}, {{Valor}} e {{LinkBoleto}} no corpo do e-mail."

---

## 2. Construção Visual e Componentização (Frontend - React)

### 2.1 Refatoração de Tela (Evolução de Prompt Falho para Sucesso)
- **Data Aproximada:** Sprint de Dashboard e Analytics
- **Prompt Falho Original:** "Refatore todo Dashboard"
  - *Análise do Erro:* O modelo não tinha o contexto do banco de dados e quebrou gráficos, tentando adivinhar layouts sem diretrizes.
- **Prompt Estruturado (Ajustado):**
  > "Atue como um desenvolvedor Front-End Sênior em React 19 e Tailwind. Refatore o componente do Dashboard (DashboardOverview.tsx) focado em performance. Garanta que o estado carregue os dados da API vindos do endpoint local http://localhost:5013 e separe visualmente os indicadores em três blocos: Prevenção, Recuperado e Orgânico. Não altere a lógica de roteamento."
- **Resultado/Aprendizado:** O prompt granular e com contexto técnico (React 19, Tailwind, porta da API) resultou em um código funcional e que exigiu poucos ajustes no ambiente local.

### 2.2 Criação de Gráficos e Funil de Cobrança
- **Objetivo:** Criar os gráficos consolidados de forma performática.
- **Prompt Utilizado:**
  > "Atue como um desenvolvedor Front-End especialista em React 19, TypeScript e Tailwind CSS. Crie um componente de Dashboard (DashboardOverview.tsx) que utilize a biblioteca Recharts ou ApexCharts. O componente deve renderizar um Funil de Recuperação exibindo os estados: 'Total a Receber', 'Total em Atraso' e 'Total Recuperado'. Os dados brutos já virão sumarizados do endpoint backend GET /api/dashboard/resumo. Inclua estados de carregamento (loading) e tratamento visual caso a API retorne vazia."

---

## 3. Depuração de Erros (Debugging Assistido)

### 3.1 Tratamento de Exceções de Data (Escopo Aberto vs Fechado)
- **Data Aproximada:** Fase de Integração e Testes
- **Prompt Falho Original:** "Ache todos os erros do projeto e conserte"
  - *Análise do Erro:* Escopo infinito causou estouro de tokens (limite de contexto), fazendo a IA alucinar arquivos inexistentes no monorepo.
- **Prompt Estruturado (Ajustado):**
  > "Analise este método específico do arquivo backend/SmartCollect.Api/Controllers/CobrancaController.cs. Ele está falhando ao realizar o parse da data dtOcorrencia que vem da API externa no formato yyyyMMdd. Corrija o bloco try-catch para tratar exceções de formato e retorne apenas o trecho corrigido."
- **Resultado/Aprendizado:** Ao fechar o escopo para um único arquivo e apontar o erro exato de *parse*, a IA resolveu o problema instantaneamente.
