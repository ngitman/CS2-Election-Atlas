import React, { useRef, useState } from "react";
import { trigger, useValue } from "cs2/api";
import { getModule } from "cs2/modding";
import { Scrollable } from "cs2/ui";
import { GROUP, EditorParty, PartyEditorState, partyEditor$ } from "./bindings";
import { Dot } from "./parts";

const TextInput = getModule<React.ComponentType<React.InputHTMLAttributes<HTMLInputElement>>>(
  "game-ui/common/input/text/text-input.tsx", "TextInput",
);
const Slider = getModule<React.ComponentType<{
  value: number;
  start: number;
  end: number;
  gamepadStep: number;
  onChange: (value: number) => void;
}>>("game-ui/common/input/slider/slider.tsx", "Slider");

type DraftParty = EditorParty & { key: string };
const makeDraft = (parties: EditorParty[]): DraftParty[] => parties.map((party) => ({ ...party, key: party.id }));
const hexColor = (color: string): string | undefined => /^#?[\da-f]{6}$/i.test(color) ? `#${color.replace(/^#/, "")}` : undefined;

/** Policy names along each axis, from -1 to 1, in five equal bands. */
type Stance = { name: string; text: string };

/** What a position means, from -1 to 1 in seven equal steps. Shown live under each slider as you drag. */
const ECONOMIC: Stance[] = [
  { name: "Socialist", text: "Key industries in public hands; the state plans investment and guarantees work." },
  { name: "Democratic socialist", text: "A large public sector, high progressive taxes and universal services." },
  { name: "Social democratic", text: "Regulated markets, strong unions and a broad welfare state." },
  { name: "Mixed economy", text: "Markets with moderate regulation and targeted public services." },
  { name: "Market liberal", text: "Lower taxes, lighter rules, public services opened to competition." },
  { name: "Free market", text: "Deregulation and privatization; government mostly enforces contracts." },
  { name: "Laissez-faire", text: "A minimal state: little taxation, regulation or welfare." },
];
const SOCIAL: Stance[] = [
  { name: "Radical progressive", text: "Pushes for rapid social change and sweeping civil liberties." },
  { name: "Progressive", text: "Champions reform, diversity and expanded rights." },
  { name: "Socially liberal", text: "Personal freedom first; light touch on social questions." },
  { name: "Moderate", text: "Balances change with continuity." },
  { name: "Socially conservative", text: "Values established institutions and gradual change." },
  { name: "Traditionalist", text: "Defends traditional values, family and community norms." },
  { name: "Strongly traditional", text: "Seeks to restore older social norms and authority." },
];
const stanceAt = (stances: Stance[], value: number) =>
  stances[Math.min(stances.length - 1, Math.max(0, Math.floor(((value + 1) / 2) * stances.length)))];

const SWATCHES = ["#cc3333", "#e8743b", "#e3b21b", "#3a9a4a", "#2f9e9e", "#3355cc", "#7a4fc4", "#666666"];

/** The panel's own buttons (the game's Button variants don't match the rest of the panel). */
const Btn = ({ label, onClick, primary, disabled }: { label: string; onClick: () => void; primary?: boolean; disabled?: boolean }) => (
  <div className={"ea-btn" + (primary ? " ea-btn--primary" : "") + (disabled ? " ea-btn--disabled" : "")} onClick={() => !disabled && onClick()}>
    {label}
  </div>
);

export const PartiesTab = ({ governingId }: { governingId?: string }) => {
  const editor = useValue(partyEditor$);
  if (!editor) return <div className="ea-empty">Loading parties…</div>;
  return <LineupEditor key={editor.revision} editor={editor} governingId={governingId} />;
};

const LineupEditor = ({ editor, governingId }: { editor: PartyEditorState; governingId?: string }) => {
  const [draft, setDraft] = useState(() => makeDraft(editor.parties));
  const [selectedKey, setSelectedKey] = useState(() => editor.parties[0].id);
  const [showMessage, setShowMessage] = useState(true);
  const nextKey = useRef(0);
  const selected = draft.find((party) => party.key === selectedKey) ?? draft[0];
  const dirty = draft.length !== editor.parties.length || draft.some((party, index) => {
    const saved = editor.parties[index];
    return party.id !== saved.id || party.name !== saved.name || party.color !== saved.color
      || party.economic !== saved.economic || party.social !== saved.social;
  });

  const change = (parties: DraftParty[]) => {
    setDraft(parties);
    setShowMessage(false);
  };
  const patch = (changes: Partial<EditorParty>) => {
    setDraft((parties) => parties.map((party) => party.key === selected.key ? { ...party, ...changes } : party));
    setShowMessage(false);
  };
  const selectPreset = (parties: EditorParty[]) => {
    change(makeDraft(parties));
    setSelectedKey(parties[0].id);
  };
  const add = () => {
    let number = draft.length + 1;
    while (draft.some((party) => party.name.trim().toLowerCase() === `party ${number}`)) number++;
    const key = `draft-${nextKey.current++}`;
    change([...draft, { key, id: "", name: `Party ${number}`, color: "#5588cc", economic: 0, social: 0 }]);
    setSelectedKey(key);
  };
  const remove = () => {
    const remaining = draft.filter((party) => party.key !== selected.key);
    change(remaining);
    setSelectedKey(remaining[0].key);
  };
  const save = () => {
    setShowMessage(true);
    const lineup = draft.map(({ id, name, color, economic, social }) => ({ id, name, color, economic, social }));
    trigger(GROUP, "saveParties", editor.revision, JSON.stringify(lineup));
  };

  return (
    <div>
      <div className="ea-party-editor">
        <div className="ea-party-editor__sidebar">
          <Scrollable vertical style={{ maxHeight: "170rem" }}>
            {draft.map((party) => (
              <div key={party.key} className={party.key === selected.key ? "ea-party-editor__item ea-party-editor__item--on" : "ea-party-editor__item"}
                onClick={() => setSelectedKey(party.key)}>
                <Dot color={hexColor(party.color) ?? "#777777"} />
                <span className="ea-ellipsis">{party.name.trim() || "Unnamed party"}</span>
              </div>
            ))}
          </Scrollable>
          <Btn label="+ Add party" disabled={draft.length >= editor.maxParties} onClick={add} />
          <Compass parties={draft} selectedKey={selected.key} onSelect={setSelectedKey} />
        </div>

        <div className="ea-party-editor__form">
          <div className="ea-card__head">
            <span className="ea-bold">Party details</span>
            {selected.id && selected.id === governingId ? <span className="ea-muted">Governing</span> : null}
          </div>
          <div className="ea-party-editor__details">
            <label className="ea-party-editor__name">
              <span className="ea-muted ea-small">Name</span>
              <TextInput className="ea-party-editor__input" value={selected.name} maxLength={editor.maxNameLength}
                onChange={(event) => patch({ name: event.currentTarget.value })} />
            </label>
            <label className="ea-party-editor__color">
              <span className="ea-muted ea-small">Hex color</span>
              <TextInput className="ea-party-editor__input" value={selected.color} maxLength={7}
                aria-invalid={!hexColor(selected.color)} onChange={(event) => patch({ color: event.currentTarget.value })} />
            </label>
          </div>
          <div className="ea-swatches">
            {SWATCHES.map((c) => (
              <div key={c} className={hexColor(selected.color)?.toLowerCase() === c ? "ea-swatch ea-swatch--on" : "ea-swatch"}
                style={{ backgroundColor: c }} onClick={() => patch({ color: c })} />
            ))}
          </div>

          <PositionField label="Economy" stances={ECONOMIC} low="Left" high="Right" value={selected.economic} onChange={(economic) => patch({ economic })} />
          <PositionField label="Society" stances={SOCIAL} low="Progressive" high="Traditional" value={selected.social} onChange={(social) => patch({ social })} />
          <div className="ea-party-editor__remove">
            <Btn label="Remove party" disabled={draft.length <= editor.minParties} onClick={remove} />
          </div>
        </div>
      </div>

      <div className="ea-party-editor__actions">
        <span className="ea-muted ea-small ea-party-editor__presets">Presets</span>
        <Btn label="Two parties" onClick={() => selectPreset(editor.twoPartyPreset)} />
        <Btn label="Four parties" onClick={() => selectPreset(editor.fourPartyPreset)} />
        <div className="ea-party-editor__spacer" />
        <Btn label="Discard edits" disabled={!dirty} onClick={() => selectPreset(editor.parties)} />
        <Btn label="Save lineup" primary disabled={!dirty} onClick={save} />
      </div>
      {showMessage && editor.message ? <div className="ea-callout ea-callout--info" role="status">{editor.message}</div> : null}
    </div>
  );
};

/** One axis: the policy name for the current value, the slider, and the two extremes. */
const PositionField = ({ label, stances, low, high, value, onChange }: {
  label: string; stances: Stance[]; low: string; high: string; value: number; onChange: (value: number) => void;
}) => {
  const stance = stanceAt(stances, value);
  return (
    <div className="ea-party-editor__axis">
      <div className="ea-card__head">
        <span className="ea-muted ea-small">{label}</span>
        <span className="ea-bold">{stance.name}</span>
      </div>
      <Slider value={value} start={-1} end={1} gamepadStep={0.05} onChange={(v) => onChange(Math.round(v * 100) / 100)} />
      <div className="ea-axis"><span>{low}</span><span>{high}</span></div>
      <div className="ea-stance">{stance.text}</div>
    </div>
  );
};

/** Every party on one map: economy left to right, society progressive (bottom) to traditional (top). Click a dot to edit it. */
const Compass = ({ parties, selectedKey, onSelect }: { parties: DraftParty[]; selectedKey: string; onSelect: (key: string) => void }) => (
  <div className="ea-compass">
    <div className="ea-muted ea-small">Traditional</div>
    <div className="ea-compass__plot">
      <div className="ea-compass__h" />
      <div className="ea-compass__v" />
      {parties.map((p) => (
        <div key={p.key} onClick={() => onSelect(p.key)}
          className={p.key === selectedKey ? "ea-compass__dot ea-compass__dot--on" : "ea-compass__dot"}
          style={{ left: `${((p.economic + 1) / 2) * 100}%`, top: `${((1 - p.social) / 2) * 100}%`, backgroundColor: hexColor(p.color) ?? "#777777" }} />
      ))}
    </div>
    <div className="ea-axis"><span>Left</span><span>Right</span></div>
    <div className="ea-muted ea-small">Progressive</div>
  </div>
);
