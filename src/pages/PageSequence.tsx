import { useEffect, useMemo, useState } from "react";
import { t } from "../i18n";
import { ICONS } from "../utils/icons";
import { colors } from "../utils/colors";
import { ChannelPills, CardHeader, Button, Modal, FormInput, FormSelect } from "../components/UI";
import {
  ApiError,
  getCollectionRules,
  getTemplates,
  createCollectionRule,
  updateCollectionRule,
  deleteCollectionRule,
  type CollectionRuleResponse,
  type MessageTemplateResponse,
  type StoredSession,
  type CreateTriggerPayload,
} from "../services/api";
import type { ShowToast } from "../types";

interface TriggerFormItem {
  templateId: string;
  channel: string;
  daysOffset: number;
  reference: string;
  order: number;
}

const newTrigger = (): TriggerFormItem => ({
  templateId: "",
  channel: "Email",
  daysOffset: 0,
  reference: "DueDate",
  order: 1,
});

export const PageSequence = ({
  showToast,
  session,
}: {
  showToast: ShowToast;
  session: StoredSession;
}) => {
  const [rules, setRules] = useState<CollectionRuleResponse[]>([]);
  const [templates, setTemplates] = useState<MessageTemplateResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [deletingRuleId, setDeletingRuleId] = useState<string | null>(null);

  const [editorOpen, setEditorOpen] = useState(false);
  const [editingRuleId, setEditingRuleId] = useState<string | null>(null);
  const [ruleName, setRuleName] = useState("");
  const [ruleDescription, setRuleDescription] = useState("");
  const [ruleActive, setRuleActive] = useState(true);
  const [triggers, setTriggers] = useState<TriggerFormItem[]>([newTrigger()]);
  const [selectedRuleId, setSelectedRuleId] = useState<string>("");

  const canEdit = session.role === "Admin" || session.role === "Worker";
  const activeRules = rules.filter(r => r.active);
  const selectedRule = rules.find(r => r.id === selectedRuleId) ?? activeRules[0] ?? rules[0];

  const templatesById = useMemo(() => {
    const map = new Map<string, MessageTemplateResponse>();
    templates.forEach(tpl => map.set(tpl.id, tpl));
    return map;
  }, [templates]);

  const toChannelPills = (channel: string): string[] => {
    const normalized = channel.trim().toLowerCase();
    if (normalized.includes("both") || normalized.includes("ambos")) return ["email", "wa"];
    if (normalized.includes("whatsapp")) return ["wa"];
    return ["email"];
  };

  const load = async () => {
    try {
      setLoading(true);
      const [r, tpl] = await Promise.all([getCollectionRules(), getTemplates()]);
      setRules(r);
      setTemplates(tpl.filter(t => t.active));
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : "Erro ao carregar régua.";
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void load();
  }, []);

  useEffect(() => {
    if (rules.length === 0) {
      setSelectedRuleId("");
      return;
    }

    if (rules.some(r => r.id === selectedRuleId))
      return;

    const preferred = rules.find(r => r.active) ?? rules[0];
    setSelectedRuleId(preferred.id);
  }, [rules, selectedRuleId]);

  const openNew = () => {
    setEditingRuleId(null);
    setRuleName("Nova Régua");
    setRuleDescription("");
    setRuleActive(false);
    setTriggers([
      {
        ...newTrigger(),
        templateId: templates[0]?.id ?? "",
      },
    ]);
    setEditorOpen(true);
  };

  const openEdit = (rule: CollectionRuleResponse) => {
    setEditingRuleId(rule.id);
    setRuleName(rule.name);
    setRuleDescription(rule.description ?? "");
    setRuleActive(rule.active);
    setTriggers(
      rule.triggers.length > 0
        ? rule.triggers
            .sort((a, b) => a.order - b.order)
            .map(tr => ({
              templateId: tr.templateId,
              channel: tr.channel,
              daysOffset: tr.daysOffset,
              reference: tr.reference,
              order: tr.order,
            }))
        : [{ ...newTrigger(), templateId: templates[0]?.id ?? "" }]
    );
    setEditorOpen(true);
  };

  const setTriggerAt = (index: number, updater: (current: TriggerFormItem) => TriggerFormItem) => {
    setTriggers(prev => prev.map((item, idx) => (idx === index ? updater(item) : item)));
  };

  const addTrigger = () => {
    const nextOrder = triggers.length + 1;
    setTriggers(prev => [
      ...prev,
      {
        ...newTrigger(),
        templateId: templates[0]?.id ?? "",
        order: nextOrder,
      },
    ]);
  };

  const removeTrigger = (index: number) => {
    setTriggers(prev => prev.filter((_, idx) => idx !== index).map((tr, idx) => ({ ...tr, order: idx + 1 })));
  };

  const handleSave = async () => {
    if (!ruleName.trim()) {
      showToast(`${ICONS.warning} Nome da régua é obrigatório.`, "warn");
      return;
    }

    if (triggers.length === 0) {
      showToast(`${ICONS.warning} Adicione ao menos um gatilho.`, "warn");
      return;
    }

    if (triggers.some(tr => !tr.templateId)) {
      showToast(`${ICONS.warning} Todo gatilho precisa de um template.`, "warn");
      return;
    }

    const payload = {
      name: ruleName.trim(),
      description: ruleDescription.trim() || undefined,
      active: ruleActive,
      triggers: triggers.map((tr, idx): CreateTriggerPayload => ({
        templateId: tr.templateId,
        channel: tr.channel,
        daysOffset: Number(tr.daysOffset),
        reference: tr.reference,
        order: idx + 1,
        active: true,
      })),
    };

    try {
      setSaving(true);
      if (editingRuleId) {
        await updateCollectionRule(editingRuleId, payload);
        showToast(`${ICONS.checkmark} Régua atualizada com sucesso!`, "success");
      } else {
        await createCollectionRule(payload);
        showToast(`${ICONS.checkmark} Régua criada com sucesso!`, "success");
      }
      setEditorOpen(false);
      await load();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : "Erro ao salvar régua.";
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setSaving(false);
    }
  };

  const handleDeleteRule = async (rule: CollectionRuleResponse) => {
    if (!canEdit)
      return;

    if (rule.isDefault) {
      showToast(`${ICONS.info} Esta é uma régua padrão e não pode ser excluída.`, "info");
      return;
    }

    const confirmed = window.confirm(`Excluir a régua \"${rule.name}\"? Esta ação não pode ser desfeita.`);
    if (!confirmed)
      return;

    try {
      setDeletingRuleId(rule.id);
      await deleteCollectionRule(rule.id);
      showToast(`${ICONS.checkmark} Régua excluída com sucesso!`, "success");
      await load();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : "Erro ao excluir régua.";
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setDeletingRuleId(null);
    }
  };

  const stopRules = [
    { color: colors.success, bg: `${colors.success}0d`, border: `${colors.success}30`, icon: ICONS.checkmark, title: t("sequence.rule.paidTitle"), desc: t("sequence.rule.paidDesc") },
    { color: colors.text3, bg: `${colors.text3}0d`, border: `${colors.text3}30`, icon: ICONS.cross, title: t("sequence.rule.cancelledTitle"), desc: t("sequence.rule.cancelledDesc") },
    { color: colors.accent, bg: `${colors.accent}08`, border: colors.border, icon: ICONS.timer, title: t("sequence.rule.frequencyTitle"), desc: t("sequence.rule.frequencyDesc") },
    { color: colors.warn, bg: `${colors.warn}0a`, border: `${colors.warn}25`, icon: ICONS.warningLight, title: t("sequence.rule.pendingTitle"), desc: t("sequence.rule.pendingDesc") },
  ];

  if (loading) return <div className="py-12 text-center text-sm text-text-muted">Carregando régua...</div>;

  return (
    <div className="animate-fade-up">
      <div className="flex justify-end items-center mb-[22px]">
        {canEdit && (
          <div className="flex gap-2">
            <Button variant="primary" onClick={openNew}>
              {ICONS.plus} Nova Régua
            </Button>
          </div>
        )}
      </div>

      <div className="grid grid-cols-2 gap-4">
        <div className="bg-surface border border-border-subtle rounded-[14px] overflow-hidden">
          <CardHeader title={<>{ICONS.timer} {t("sequence.sendTriggers")}</>} subtitle={t("sequence.triggerSubtitle")} />
          <div className="p-5 relative">
            {selectedRule && selectedRule.triggers.length > 0 ? (
              <>
                <div
                  className="absolute left-[40px] top-[40px] bottom-[40px] w-[2px] z-0"
                  style={{ background: `linear-gradient(to bottom, ${colors.accent}, ${colors.accent})` }}
                />
                <div className="flex flex-col gap-0">
                  {selectedRule.triggers.map((tr, i) => (
                    <div key={tr.id ?? i} className="flex items-center gap-3.5 py-2.5 relative">
                      <div
                        className="w-[42px] h-[42px] rounded-full shrink-0 z-[1] flex items-center justify-center font-extrabold text-xs"
                        style={{ background: `${colors.accent}18`, border: `2px solid ${colors.accent}`, color: colors.accent }}
                      >
                        {tr.daysOffset >= 0 ? `D+${tr.daysOffset}` : `D${tr.daysOffset}`}
                      </div>
                      <div className="flex-1 bg-surface-2 rounded-[10px] px-[15px] py-3 border border-border-subtle flex items-center justify-between">
                        <div>
                          <div className="text-[11px] text-text-muted mb-0.5">{tr.templateName ?? "Template"}</div>
                          <div className="text-[13.5px] font-semibold">
                            {tr.daysOffset >= 0 ? `${tr.daysOffset} dias após vencimento` : `${Math.abs(tr.daysOffset)} dias antes do vencimento`}
                          </div>
                        </div>
                        <ChannelPills channels={toChannelPills(tr.channel)} />
                      </div>
                    </div>
                  ))}
                </div>
              </>
            ) : (
              <div className="text-center py-8 text-sm text-text-muted">
                <div className="text-2xl mb-2">{ICONS.timer}</div>
                {rules.length > 0 ? "Selecione uma régua abaixo para visualizar os gatilhos." : "Nenhuma régua configurada ainda."}
              </div>
            )}
          </div>
        </div>

        <div className="bg-surface border border-border-subtle rounded-[14px] overflow-hidden">
          <CardHeader title={<>{ICONS.stop} {t("sequence.stopRules")}</>} />
          <div className="p-5 flex flex-col gap-3">
            {stopRules.map((rule, i) => (
              <div key={i} className="rounded-[10px] p-3.5" style={{ background: rule.bg, border: `1px solid ${rule.border}` }}>
                <div className="font-bold mb-[5px]" style={{ color: rule.color }}>{rule.icon} {rule.title}</div>
                <div className="text-xs text-text-secondary leading-relaxed">{rule.desc}</div>
              </div>
            ))}
          </div>

          {rules.length > 0 && (
            <div className="px-5 pb-5">
              <div className="text-[11px] font-bold uppercase text-text-muted mb-2 tracking-wider">Todas as Réguas</div>
              <div className="flex flex-col gap-2">
                {rules.map(rule => (
                  <div
                    key={rule.id}
                    onClick={() => setSelectedRuleId(rule.id)}
                    className={`flex items-center justify-between px-3 py-2 rounded-lg border cursor-pointer ${selectedRule?.id === rule.id ? "border-accent bg-accent/10" : rule.active ? "border-accent/30 bg-accent/5" : "border-border-subtle bg-surface-2"}`}
                  >
                    <div>
                      <div className="text-sm font-semibold flex items-center gap-2">
                        <span>{rule.name}</span>
                        {rule.isDefault && (
                          <span className="text-[10px] font-bold uppercase tracking-wide text-accent bg-accent/10 px-1.5 py-0.5 rounded-full">
                            Régua padrão
                          </span>
                        )}
                      </div>
                      <div className="text-xs text-text-muted">{rule.triggers.length} gatilhos</div>
                    </div>
                    <div className="flex items-center gap-2">
                      {rule.active && <span className="text-[11px] font-bold text-success bg-success/10 px-2 py-0.5 rounded-full">Ativa</span>}
                      {canEdit && (
                        <>
                          <Button size="sm" variant="secondary" onClick={(e) => { e.stopPropagation(); openEdit(rule); }}>
                            {ICONS.pencil} Editar
                          </Button>
                          <Button
                            size="sm"
                            variant="danger"
                            onClick={(e) => { e.stopPropagation(); void handleDeleteRule(rule); }}
                            disabled={deletingRuleId === rule.id || rule.isDefault}
                          >
                            {rule.isDefault ? "Padrão" : deletingRuleId === rule.id ? "Excluindo..." : "Excluir"}
                          </Button>
                        </>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      </div>

      <Modal
        open={editorOpen}
        onClose={() => setEditorOpen(false)}
        title={editingRuleId ? "Editar Régua" : "Nova Régua"}
        maxWidth={860}
        footer={<>
          <Button variant="secondary" onClick={() => setEditorOpen(false)}>Cancelar</Button>
          <Button variant="primary" onClick={handleSave}>{saving ? "Salvando..." : "Salvar Régua"}</Button>
        </>}
      >
        <div className="grid grid-cols-2 gap-3.5 mb-4">
          <FormInput label="Nome" value={ruleName} onChange={e => setRuleName(e.target.value)} />
          <div className="flex items-center justify-start mt-6">
            <button
              type="button"
              role="switch"
              aria-checked={ruleActive}
              onClick={() => setRuleActive(prev => !prev)}
              className={`inline-flex items-center gap-2 rounded-full border px-2 py-1 text-xs font-semibold transition-colors ${ruleActive ? "border-success/30 bg-success/12 text-success" : "border-border-subtle-2 bg-surface-2 text-text-muted"}`}
            >
              <span className={`h-4 w-7 rounded-full p-[2px] transition-colors ${ruleActive ? "bg-success/75" : "bg-text-muted/40"}`}>
                <span className={`block h-3 w-3 rounded-full bg-white transition-transform ${ruleActive ? "translate-x-3" : "translate-x-0"}`} />
              </span>
              <span>Régua ativa</span>
            </button>
          </div>
          <div className="col-span-2">
            <FormInput label="Descrição" value={ruleDescription} onChange={e => setRuleDescription(e.target.value)} />
          </div>
        </div>

        <div className="rounded-xl border border-border-subtle overflow-hidden">
          <table className="w-full border-collapse text-[13px]">
            <thead>
              <tr className="bg-surface-2">
                <th className="px-3 py-2 text-left text-xs font-bold uppercase text-text-muted">Template</th>
                <th className="px-3 py-2 text-left text-xs font-bold uppercase text-text-muted">Canal</th>
                <th className="px-3 py-2 text-left text-xs font-bold uppercase text-text-muted">Referência</th>
                <th className="px-3 py-2 text-left text-xs font-bold uppercase text-text-muted">Offset</th>
                <th className="px-3 py-2 text-left text-xs font-bold uppercase text-text-muted">Ações</th>
              </tr>
            </thead>
            <tbody>
              {triggers.map((tr, idx) => (
                <tr key={idx} className="border-b border-border-subtle">
                  <td className="px-3 py-2">
                    <select
                      value={tr.templateId}
                      onChange={e => {
                        const nextTemplateId = e.target.value;
                        const tpl = templatesById.get(nextTemplateId);
                        setTriggerAt(idx, old => ({ ...old, templateId: nextTemplateId, channel: tpl?.channel ?? old.channel }));
                      }}
                      className="bg-surface-2 border border-border-subtle-2 rounded-lg px-2 py-1.5 text-xs w-full"
                    >
                      <option value="">Selecione...</option>
                      {templates.map(tpl => (
                        <option key={tpl.id} value={tpl.id}>{tpl.name}</option>
                      ))}
                    </select>
                  </td>
                  <td className="px-3 py-2">
                    <FormSelect value={tr.channel} onChange={e => setTriggerAt(idx, old => ({ ...old, channel: e.target.value }))}>
                      <option value="Email">Email</option>
                      <option value="WhatsApp">WhatsApp</option>
                      <option value="Both">Ambos</option>
                    </FormSelect>
                  </td>
                  <td className="px-3 py-2">
                    <FormSelect value={tr.reference} onChange={e => setTriggerAt(idx, old => ({ ...old, reference: e.target.value }))}>
                      <option value="DueDate">Vencimento</option>
                      <option value="IssueDate">Emissão</option>
                    </FormSelect>
                  </td>
                  <td className="px-3 py-2">
                    <FormInput type="number" value={String(tr.daysOffset)} onChange={e => setTriggerAt(idx, old => ({ ...old, daysOffset: Number(e.target.value || 0) }))} />
                  </td>
                  <td className="px-3 py-2">
                    <Button size="sm" variant="danger" onClick={() => removeTrigger(idx)} disabled={triggers.length === 1}>Remover</Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <div className="mt-3 flex justify-end">
          <Button size="sm" variant="secondary" onClick={addTrigger}>{ICONS.plus} Adicionar Gatilho</Button>
        </div>
      </Modal>
    </div>
  );
};
