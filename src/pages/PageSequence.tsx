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
  selectedTenantId,
}: {
  showToast: ShowToast;
  session: StoredSession;
  selectedTenantId?: string;
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

  const tenantId = session.role === "Master" ? selectedTenantId : undefined;
  const requiresTenantSelection = session.role === "Master" && !tenantId;
  const canEdit =
    session.role === "Admin"
    || session.role === "Worker"
    || (session.role === "Master" && Boolean(tenantId));
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
    if (requiresTenantSelection) {
      setRules([]);
      setTemplates([]);
      setLoading(false);
      return;
    }

    try {
      setLoading(true);
      const [r, tpl] = await Promise.all([getCollectionRules(tenantId), getTemplates(tenantId)]);
      setRules(r);
      setTemplates(tpl.filter(t => t.active));
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t("sequence.errors.load");
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void load();
  }, [tenantId, requiresTenantSelection]);

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
    setRuleName(t("sequence.newRuleName"));
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
    if (requiresTenantSelection) {
      showToast(`${ICONS.warning} ${t("sequence.validation.selectTenant")}`, "warn");
      return;
    }

    if (!ruleName.trim()) {
      showToast(`${ICONS.warning} ${t("sequence.validation.nameRequired")}`, "warn");
      return;
    }

    if (triggers.length === 0) {
      showToast(`${ICONS.warning} ${t("sequence.validation.addTrigger")}`, "warn");
      return;
    }

    if (triggers.some(tr => !tr.templateId)) {
      showToast(`${ICONS.warning} ${t("sequence.validation.triggerNeedsTemplate")}`, "warn");
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
        await updateCollectionRule(editingRuleId, payload, tenantId);
        showToast(`${ICONS.checkmark} ${t("sequence.messages.updated")}`, "success");
      } else {
        await createCollectionRule(payload, tenantId);
        showToast(`${ICONS.checkmark} ${t("sequence.messages.created")}`, "success");
      }
      setEditorOpen(false);
      await load();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t("sequence.errors.save");
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setSaving(false);
    }
  };

  const handleDeleteRule = async (rule: CollectionRuleResponse) => {
    if (!canEdit)
      return;

    if (requiresTenantSelection) {
      showToast(`${ICONS.warning} ${t("sequence.validation.selectTenantDelete")}`, "warn");
      return;
    }

    if (rule.isDefault) {
      showToast(`${ICONS.info} ${t("sequence.validation.cannotDeleteDefault")}`, "info");
      return;
    }

    const confirmed = window.confirm(`${t("sequence.confirmDelete")} "${rule.name}"? ${t("sequence.confirmDeleteWarn")}`);
    if (!confirmed)
      return;

    try {
      setDeletingRuleId(rule.id);
      await deleteCollectionRule(rule.id, tenantId);
      showToast(`${ICONS.checkmark} ${t("sequence.messages.deleted")}`, "success");
      await load();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t("sequence.errors.delete");
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

  if (loading) return <div className="py-12 text-center text-sm text-text-muted">{t("sequence.loading")}</div>;

  return (
    <div className="animate-fade-up">
      {requiresTenantSelection && (
        <div className="rounded-xl border border-border-subtle bg-surface-2/60 px-4 py-3 text-sm text-text-secondary mb-4">
          {t("sequence.validation.selectTenant")}
        </div>
      )}

      <div className="flex justify-end items-center mb-[22px]">
        {canEdit && (
          <div className="flex gap-2">
            <Button variant="primary" onClick={openNew} disabled={requiresTenantSelection}>
              {ICONS.plus} {t("sequence.newRuleButton")}
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
                          <div className="text-[11px] text-text-muted mb-0.5">{tr.templateName ?? t("sequence.templateFallback")}</div>
                          <div className="text-[13.5px] font-semibold">
                            {tr.daysOffset >= 0
                              ? `${tr.daysOffset} ${t("sequence.daysAfterDue")}`
                              : `${Math.abs(tr.daysOffset)} ${t("sequence.daysBeforeDue")}`}
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
                {rules.length > 0 ? t("sequence.selectRulePrompt") : t("sequence.noRulesPrompt")}
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
              <div className="text-[11px] font-bold uppercase text-text-muted mb-2 tracking-wider">{t("sequence.allRulesTitle")}</div>
              <div className="flex flex-col gap-2">
                {rules.map(rule => (
                  <div
                    key={rule.id}
                    onClick={() => setSelectedRuleId(rule.id)}
                    className={`flex items-center justify-between px-3 py-2 rounded-lg border cursor-pointer ${
                      selectedRule?.id === rule.id
                        ? "border-accent bg-accent/14 shadow-[inset_0_0_0_1px_rgba(59,130,246,0.25)]"
                        : rule.active
                          ? "border-success/40 bg-success/8"
                          : "border-border-subtle bg-surface-2"
                    }`}
                  >
                    <div>
                      <div className="text-sm font-semibold flex items-center gap-2">
                        <span>{rule.name}</span>
                        {rule.isDefault && (
                          <span className="text-[10px] font-bold uppercase tracking-wide text-accent bg-accent/10 px-1.5 py-0.5 rounded-full">
                            {t("sequence.defaultBadge")}
                          </span>
                        )}
                      </div>
                      <div className="text-xs text-text-muted">{rule.triggers.length} {t("sequence.triggersCountLabel")}</div>
                    </div>
                    <div className="flex items-center gap-2">
                      {rule.active && <span className="text-[11px] font-bold text-success bg-success/10 px-2 py-0.5 rounded-full">{t("sequence.activeBadge")}</span>}
                      {canEdit && (
                        <>
                          <Button size="sm" variant="secondary" onClick={(e) => { e.stopPropagation(); openEdit(rule); }}>
                            {ICONS.pencil} {t("common.edit")}
                          </Button>
                          <Button
                            size="sm"
                            variant="danger"
                            onClick={(e) => { e.stopPropagation(); void handleDeleteRule(rule); }}
                            disabled={deletingRuleId === rule.id || rule.isDefault}
                          >
                            {rule.isDefault ? t("sequence.actionDefault") : deletingRuleId === rule.id ? t("sequence.actionDeleting") : t("sequence.actionDelete")}
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
        title={editingRuleId ? t("sequence.modalEditTitle") : t("sequence.modalNewTitle")}
        maxWidth={860}
        footer={<>
          <Button variant="secondary" onClick={() => setEditorOpen(false)}>{t("common.cancel")}</Button>
          <Button variant="primary" onClick={handleSave}>{saving ? t("common.saving") : t("sequence.saveSequence")}</Button>
        </>}
      >
        <div className="grid grid-cols-2 gap-3.5 mb-4">
          <FormInput label={t("sequence.ruleNameLabel")} value={ruleName} onChange={e => setRuleName(e.target.value)} />
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
              <span>{t("sequence.ruleActiveLabel")}</span>
            </button>
          </div>
          <div className="col-span-2">
            <FormInput label={t("sequence.ruleDescriptionLabel")} value={ruleDescription} onChange={e => setRuleDescription(e.target.value)} />
          </div>
        </div>

        <div className="rounded-xl border border-border-subtle overflow-hidden">
          <table className="w-full border-collapse text-[13px]">
            <thead>
              <tr className="bg-surface-2">
                <th className="px-3 py-2 text-left text-xs font-bold uppercase text-text-muted">{t("sequence.table.template")}</th>
                <th className="px-3 py-2 text-left text-xs font-bold uppercase text-text-muted">{t("sequence.table.channel")}</th>
                <th className="px-3 py-2 text-left text-xs font-bold uppercase text-text-muted">{t("sequence.table.reference")}</th>
                <th className="px-3 py-2 text-left text-xs font-bold uppercase text-text-muted">{t("sequence.table.offset")}</th>
                <th className="px-3 py-2 text-left text-xs font-bold uppercase text-text-muted">{t("sequence.table.actions")}</th>
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
                      <option value="">{t("sequence.selectTemplatePlaceholder")}</option>
                      {templates.map(tpl => (
                        <option key={tpl.id} value={tpl.id}>{tpl.name}</option>
                      ))}
                    </select>
                  </td>
                  <td className="px-3 py-2">
                    <FormSelect value={tr.channel} onChange={e => setTriggerAt(idx, old => ({ ...old, channel: e.target.value }))}>
                      <option value="Email">{t("channel.email")}</option>
                      <option value="WhatsApp">{t("channel.whatsapp")}</option>
                      <option value="Both">{t("common.both")}</option>
                    </FormSelect>
                  </td>
                  <td className="px-3 py-2">
                    <FormSelect value={tr.reference} onChange={e => setTriggerAt(idx, old => ({ ...old, reference: e.target.value }))}>
                      <option value="DueDate">{t("sequence.referenceDueDate")}</option>
                      <option value="IssueDate">{t("sequence.referenceIssueDate")}</option>
                    </FormSelect>
                  </td>
                  <td className="px-3 py-2">
                    <FormInput type="number" value={String(tr.daysOffset)} onChange={e => setTriggerAt(idx, old => ({ ...old, daysOffset: Number(e.target.value || 0) }))} />
                  </td>
                  <td className="px-3 py-2">
                    <Button size="sm" variant="danger" onClick={() => removeTrigger(idx)} disabled={triggers.length === 1}>{t("sequence.actionRemove")}</Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <div className="mt-3 flex justify-end">
          <Button size="sm" variant="secondary" onClick={addTrigger}>{ICONS.plus} {t("sequence.addTrigger")}</Button>
        </div>
      </Modal>
    </div>
  );
};
