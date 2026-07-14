import { Ionicons } from '@expo/vector-icons';
import { useState } from 'react';
import { Alert, Pressable, StyleSheet, View } from 'react-native';
import { Button } from '../../components/Button';
import { StatusPill } from '../../components/StatusPill';
import { Text } from '../../components/Text';
import { TextField } from '../../components/TextField';
import { radius, spacing } from '../../components/theme';
import { useAppTheme } from '../../context/ThemeContext';
import { ordersApi } from '../../api/orders';
import type { OrderLine, Refund, Return, ReturnStatus, SaleFinancials } from '../../domain/order.types';
import { formatMoney } from '../../utils/money';

export type LineEvents = {
  refunds: Refund[];
  returns: Return[];
};

export type MutationRunner = (fn: () => Promise<unknown>) => Promise<boolean>;

const openReturnStatuses: ReturnStatus[] = ['Requested', 'LabelIssued', 'Received'];
const returnStages: { key: ReturnStatus; label: string }[] = [
  { key: 'Requested', label: 'Requested' },
  { key: 'LabelIssued', label: 'Label' },
  { key: 'Received', label: 'Received' },
  { key: 'Refunded', label: 'Refunded' },
];

const day = (value: string | null | undefined) =>
  value ? new Intl.DateTimeFormat('en-US', { dateStyle: 'medium' }).format(new Date(value)) : '—';

const moneyInput = (value: number) => value.toFixed(2);

function parseAmount(value: string): number {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : Number.NaN;
}

export function OrderLineRow({
  line,
  events,
  busy,
  runMutation,
}: {
  line: OrderLine;
  events: LineEvents;
  busy: boolean;
  runMutation: MutationRunner;
}) {
  const { colors: c } = useAppTheme();
  const [expanded, setExpanded] = useState(false);
  const sale = line.sale;
  const fees = (sale?.platformFees ?? 0) + (sale?.paymentFees ?? 0);
  const refunded = events.refunds.reduce((sum, refund) => sum + refund.itemAmount + refund.shippingRefunded, 0);
  const hasActivity = events.refunds.length > 0 || events.returns.length > 0;

  return (
    <View style={[styles.lineRow, { borderTopColor: c.borderLight }]}>
      <Pressable
        onPress={() => sale ? setExpanded((value) => !value) : undefined}
        disabled={!sale}
        style={({ pressed }) => [styles.linePress, sale && pressed && { opacity: 0.8 }]}
        accessibilityRole={sale ? 'button' : undefined}
        accessibilityLabel={sale ? `${expanded ? 'Collapse' : 'Expand'} ${line.titleSnapshot}` : line.titleSnapshot}
      >
        <View style={styles.lineHead}>
          <Text variant="bodyBold" numberOfLines={2} style={styles.flex}>{line.titleSnapshot}</Text>
          <View style={styles.lineRight}>
            <Text variant="bodyBold">{formatMoney(sale?.netProfit ?? 0)}</Text>
            {sale ? <Ionicons name={expanded ? 'chevron-up' : 'chevron-down'} size={16} color={c.textMuted} /> : null}
          </View>
        </View>
        <View style={styles.lineMeta}>
          <Text variant="caption" color={c.textMuted}>Sold {formatMoney(line.unitPrice)}</Text>
          {fees > 0 ? <Text variant="caption" color={c.textMuted}>Fees -{formatMoney(fees)}</Text> : null}
          <Text variant="caption" color={c.textMuted}>COGS {formatMoney(sale?.costOfGoods ?? line.unitCostSnapshot ?? 0)}</Text>
          {refunded > 0 ? <Text variant="caption" color={c.warning}>Refunded -{formatMoney(refunded)}</Text> : null}
          {hasActivity ? <Text variant="caption" color={c.primary}>{events.returns.length} return · {events.refunds.length} refund</Text> : null}
        </View>
        {sale ? <Text variant="label" color={c.textMuted}>Tap to manage this sale line</Text> : <Text variant="caption" color={c.textMuted}>No sale line</Text>}
      </Pressable>
      {sale && expanded ? (
        <LineWorkspace
          sale={sale}
          refunds={events.refunds}
          returns={events.returns}
          busy={busy}
          runMutation={runMutation}
        />
      ) : null}
    </View>
  );
}

function LineWorkspace({
  sale,
  refunds,
  returns,
  busy,
  runMutation,
}: {
  sale: SaleFinancials;
  refunds: Refund[];
  returns: Return[];
  busy: boolean;
  runMutation: MutationRunner;
}) {
  const { colors: c } = useAppTheme();
  const [mode, setMode] = useState<'idle' | 'refund' | 'return'>('idle');
  const refundedItem = refunds.reduce((sum, refund) => sum + refund.itemAmount, 0);
  const remaining = Math.max(0, Math.round((sale.soldPrice - refundedItem) * 100) / 100);
  const openReturn = returns.find((item) => openReturnStatuses.includes(item.status));
  const history = returns.filter((item) => item !== openReturn);

  return (
    <View style={[styles.workspace, { backgroundColor: c.surfaceAlt, borderColor: c.borderLight }]}>
      <RefundHistory saleId={sale.id} refunds={refunds} busy={busy} runMutation={runMutation} />

      {openReturn ? (
        <ReturnFlow rma={openReturn} remaining={remaining} busy={busy} runMutation={runMutation} />
      ) : mode === 'refund' ? (
        <RefundForm sale={sale} remaining={remaining} busy={busy} runMutation={runMutation} onClose={() => setMode('idle')} />
      ) : mode === 'return' ? (
        <ReturnRequestForm saleId={sale.id} busy={busy} runMutation={runMutation} onClose={() => setMode('idle')} />
      ) : (
        <View style={styles.actions}>
          <Button label="Issue refund" size="sm" disabled={busy || remaining <= 0} onPress={() => setMode('refund')} />
          <Button label="Start return" icon="return-up-back-outline" size="sm" variant="secondary" disabled={busy} onPress={() => setMode('return')} />
          {remaining <= 0 ? <Text variant="caption" color={c.textMuted}>Fully refunded.</Text> : null}
        </View>
      )}

      {history.length > 0 ? (
        <View style={styles.returnHistory}>
          {history.map((item) => (
            <StatusPill
              key={item.id}
              label={`Return ${item.status}${item.restocked ? ' · restocked' : ''}`}
              tone={item.status === 'Refunded' ? 'success' : item.status === 'Rejected' ? 'danger' : 'neutral'}
              size="sm"
            />
          ))}
        </View>
      ) : null}
    </View>
  );
}

function RefundForm({
  sale,
  remaining,
  busy,
  runMutation,
  onClose,
}: {
  sale: SaleFinancials;
  remaining: number;
  busy: boolean;
  runMutation: MutationRunner;
  onClose: () => void;
}) {
  const { colors: c } = useAppTheme();
  const [amount, setAmount] = useState(moneyInput(remaining));
  const [reason, setReason] = useState('');
  const [shippingRefunded, setShippingRefunded] = useState('0.00');
  const [taxRefunded, setTaxRefunded] = useState('0.00');
  const [feeCredit, setFeeCredit] = useState('0.00');
  const [notes, setNotes] = useState('');
  const [advanced, setAdvanced] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const itemAmount = parseAmount(amount);
  const isFull = Number.isFinite(itemAmount) && itemAmount >= remaining - 0.001 && itemAmount > 0;

  async function submit() {
    const parsedItem = parseAmount(amount);
    const parsedShipping = parseAmount(shippingRefunded || '0');
    const parsedTax = parseAmount(taxRefunded || '0');
    const parsedFee = parseAmount(feeCredit || '0');
    if (!reason.trim()) {
      setFormError('Reason is required.');
      return;
    }
    if (![parsedItem, parsedShipping, parsedTax, parsedFee].every((value) => Number.isFinite(value) && value >= 0)) {
      setFormError('Refund amounts must be valid non-negative numbers.');
      return;
    }
    if (parsedItem > remaining + 0.01) {
      setFormError(`Item refund cannot exceed ${formatMoney(remaining)}.`);
      return;
    }
    if (parsedItem + parsedShipping + parsedTax + parsedFee <= 0) {
      setFormError('Enter at least one non-zero refund amount.');
      return;
    }

    setFormError(null);
    const ok = await runMutation(() => ordersApi.recordRefund(sale.id, {
      type: isFull ? 'Full' : 'Partial',
      itemAmount: parsedItem,
      shippingRefunded: parsedShipping,
      taxRefunded: parsedTax,
      feeCredit: parsedFee,
      itemReturned: false,
      refundedAt: new Date().toISOString(),
      reason: reason.trim(),
      notes: notes.trim() || null,
    }));
    if (ok) onClose();
  }

  return (
    <View style={[styles.formCard, { backgroundColor: c.surface, borderColor: c.border }]}>
      <View style={styles.formHeader}>
        <View>
          <Text variant="bodyBold">Issue refund</Text>
          <Text variant="caption">Refundable item amount {formatMoney(remaining)}</Text>
        </View>
        <StatusPill label={isFull ? 'Full' : 'Partial'} tone={isFull ? 'danger' : 'warning'} size="sm" />
      </View>
      <TextField label="Item amount" value={amount} onChangeText={setAmount} keyboardType="decimal-pad" prefix="$" />
      <TextField label="Reason" value={reason} onChangeText={setReason} placeholder="e.g. Buyer kept it, partial courtesy refund" />
      <Pressable onPress={() => setAdvanced((value) => !value)} hitSlop={8}>
        <Text variant="caption" color={c.primary}>{advanced ? 'Hide' : 'Adjust'} shipping, tax and fee credit</Text>
      </Pressable>
      {advanced ? (
        <View style={styles.advancedGrid}>
          <TextField label="Shipping refunded" value={shippingRefunded} onChangeText={setShippingRefunded} keyboardType="decimal-pad" prefix="$" />
          <TextField label="Tax refunded" value={taxRefunded} onChangeText={setTaxRefunded} keyboardType="decimal-pad" prefix="$" />
          <TextField label="Fee credit" value={feeCredit} onChangeText={setFeeCredit} keyboardType="decimal-pad" prefix="$" />
          <TextField label="Notes" value={notes} onChangeText={setNotes} multiline placeholder="Optional internal note" />
        </View>
      ) : null}
      {formError ? <Text variant="caption" color={c.danger}>{formError}</Text> : null}
      <View style={styles.actions}>
        <Button label="Cancel" size="sm" variant="ghost" disabled={busy} onPress={onClose} />
        <Button label={`Refund ${formatMoney(Number.isFinite(itemAmount) ? itemAmount : 0)}`} size="sm" disabled={busy} onPress={() => void submit()} />
      </View>
    </View>
  );
}

function ReturnRequestForm({
  saleId,
  busy,
  runMutation,
  onClose,
}: {
  saleId: string;
  busy: boolean;
  runMutation: MutationRunner;
  onClose: () => void;
}) {
  const { colors: c } = useAppTheme();
  const [reason, setReason] = useState('');
  const [notes, setNotes] = useState('');
  const [formError, setFormError] = useState<string | null>(null);

  async function submit() {
    if (!reason.trim()) {
      setFormError('Reason is required.');
      return;
    }
    setFormError(null);
    const ok = await runMutation(() => ordersApi.openReturn(saleId, { reason: reason.trim(), notes: notes.trim() || null }));
    if (ok) onClose();
  }

  return (
    <View style={[styles.formCard, { backgroundColor: c.surface, borderColor: c.border }]}>
      <Text variant="bodyBold">Start return</Text>
      <TextField label="Reason" value={reason} onChangeText={setReason} multiline placeholder="e.g. Does not fit, buyer requested return" />
      <TextField label="Notes" value={notes} onChangeText={setNotes} multiline placeholder="Optional internal note" />
      <Text variant="caption" color={c.textMuted}>Open the RMA first. Refund is issued after the item is received.</Text>
      {formError ? <Text variant="caption" color={c.danger}>{formError}</Text> : null}
      <View style={styles.actions}>
        <Button label="Cancel" size="sm" variant="ghost" disabled={busy} onPress={onClose} />
        <Button label="Open return" size="sm" disabled={busy} onPress={() => void submit()} />
      </View>
    </View>
  );
}

function ReturnFlow({
  rma,
  remaining,
  busy,
  runMutation,
}: {
  rma: Return;
  remaining: number;
  busy: boolean;
  runMutation: MutationRunner;
}) {
  const { colors: c } = useAppTheme();
  const [labelCost, setLabelCost] = useState(moneyInput(rma.returnShippingCost || 0));
  const [refundOpen, setRefundOpen] = useState(false);
  const [exceptionOpen, setExceptionOpen] = useState(false);
  const [rejectReason, setRejectReason] = useState('');
  const [formError, setFormError] = useState<string | null>(null);

  async function issueLabel() {
    const cost = parseAmount(labelCost || '0');
    if (!Number.isFinite(cost) || cost < 0) {
      setFormError('Return label cost must be a valid non-negative amount.');
      return;
    }
    setFormError(null);
    await runMutation(() => ordersApi.issueReturnLabel(rma.id, { returnShippingCost: cost }));
  }

  async function reject() {
    if (!rejectReason.trim()) {
      setFormError('Reject reason is required.');
      return;
    }
    setFormError(null);
    await runMutation(() => ordersApi.rejectReturn(rma.id, rejectReason.trim()));
  }

  return (
    <View style={[styles.formCard, { backgroundColor: c.surface, borderColor: c.border }]}>
      <View style={styles.formHeader}>
        <View>
          <Text variant="bodyBold">Return in progress</Text>
          <Text variant="caption">Opened {day(rma.requestedAt)}</Text>
        </View>
        <StatusPill label={rma.status} tone={rma.status === 'Refunded' ? 'success' : rma.status === 'Rejected' ? 'danger' : 'info'} size="sm" />
      </View>

      <ReturnStageTracker status={rma.status} />
      <Text variant="caption">Reason: {rma.reason}</Text>
      {rma.returnShippingCost > 0 ? <Text variant="caption" color={c.textMuted}>Return label cost {formatMoney(rma.returnShippingCost)}</Text> : null}

      {rma.status === 'Requested' ? (
        <View style={styles.formStack}>
          <TextField label="Return label cost" value={labelCost} onChangeText={setLabelCost} keyboardType="decimal-pad" prefix="$" />
          <View style={styles.actions}>
            <Button label="Send label" size="sm" disabled={busy} onPress={() => void issueLabel()} />
            <Button label="Mark received" size="sm" variant="secondary" disabled={busy} onPress={() => void runMutation(() => ordersApi.receiveReturn(rma.id))} />
          </View>
        </View>
      ) : null}

      {rma.status === 'LabelIssued' ? (
        <Button label="Mark received back" size="sm" disabled={busy} onPress={() => void runMutation(() => ordersApi.receiveReturn(rma.id))} />
      ) : null}

      {rma.status === 'Received' && !refundOpen ? (
        <Button label="Refund / resolve return" icon="refresh-outline" size="sm" disabled={busy} onPress={() => setRefundOpen(true)} />
      ) : null}

      {rma.status === 'Received' && refundOpen ? (
        <ReturnRefundForm rma={rma} remaining={remaining} busy={busy} runMutation={runMutation} onClose={() => setRefundOpen(false)} />
      ) : null}

      {openReturnStatuses.includes(rma.status) ? (
        <View style={[styles.exceptionBox, { borderTopColor: c.borderLight }]}>
          <Pressable onPress={() => setExceptionOpen((value) => !value)} hitSlop={8}>
            <Text variant="caption" color={c.textMuted}>{exceptionOpen ? 'Hide exceptions' : 'Reject or cancel return'}</Text>
          </Pressable>
          {exceptionOpen ? (
            <View style={styles.formStack}>
              <TextField label="Reject reason" value={rejectReason} onChangeText={setRejectReason} placeholder="Required only for reject" />
              <View style={styles.actions}>
                <Button label="Reject" size="sm" variant="danger" disabled={busy} onPress={() => void reject()} />
                <Button label="Cancel return" size="sm" variant="ghost" disabled={busy} onPress={() => void runMutation(() => ordersApi.cancelReturn(rma.id))} />
              </View>
            </View>
          ) : null}
        </View>
      ) : null}

      {formError ? <Text variant="caption" color={c.danger}>{formError}</Text> : null}
    </View>
  );
}

function ReturnRefundForm({
  rma,
  remaining,
  busy,
  runMutation,
  onClose,
}: {
  rma: Return;
  remaining: number;
  busy: boolean;
  runMutation: MutationRunner;
  onClose: () => void;
}) {
  const { colors: c } = useAppTheme();
  const [amount, setAmount] = useState(moneyInput(remaining));
  const [restock, setRestock] = useState(true);
  const [formError, setFormError] = useState<string | null>(null);
  const itemAmount = parseAmount(amount);
  const isFull = Number.isFinite(itemAmount) && itemAmount >= remaining - 0.001 && itemAmount > 0;

  async function submit() {
    const parsed = parseAmount(amount);
    if (!Number.isFinite(parsed) || parsed < 0) {
      setFormError('Refund amount must be a valid non-negative number.');
      return;
    }
    if (parsed > remaining + 0.01) {
      setFormError(`Item refund cannot exceed ${formatMoney(remaining)}.`);
      return;
    }
    if (parsed <= 0) {
      setFormError('Enter a non-zero refund amount.');
      return;
    }
    setFormError(null);
    const ok = await runMutation(() => ordersApi.resolveReturn(rma.id, {
      itemAmount: parsed,
      type: isFull ? 'Full' : 'Partial',
      restock,
      reason: rma.reason,
    }));
    if (ok) onClose();
  }

  return (
    <View style={[styles.returnRefundBox, { backgroundColor: c.surfaceAlt }]}>
      <View style={styles.formHeader}>
        <Text variant="bodyBold">Resolve return</Text>
        <StatusPill label={isFull ? 'Full' : 'Partial'} tone={isFull ? 'danger' : 'warning'} size="sm" />
      </View>
      <TextField label="Item amount to refund" value={amount} onChangeText={setAmount} keyboardType="decimal-pad" prefix="$" />
      <Pressable
        onPress={() => setRestock((value) => !value)}
        style={[styles.toggleRow, { borderColor: restock ? c.success : c.border, backgroundColor: restock ? c.successLight : c.surface }]}
      >
        <Ionicons name={restock ? 'checkbox' : 'square-outline'} size={18} color={restock ? c.successDark : c.textMuted} />
        <Text variant="caption" color={restock ? c.successDark : c.textSecondary}>
          {restock ? 'Restock item into inventory' : 'Refund without restocking'}
        </Text>
      </Pressable>
      {formError ? <Text variant="caption" color={c.danger}>{formError}</Text> : null}
      <View style={styles.actions}>
        <Button label="Cancel" size="sm" variant="ghost" disabled={busy} onPress={onClose} />
        <Button label={`Refund ${formatMoney(Number.isFinite(itemAmount) ? itemAmount : 0)}`} size="sm" disabled={busy} onPress={() => void submit()} />
      </View>
    </View>
  );
}

function ReturnStageTracker({ status }: { status: ReturnStatus }) {
  const { colors: c } = useAppTheme();
  const currentIndex = returnStages.findIndex((stage) => stage.key === status);
  const terminalRefunded = status === 'Refunded';

  return (
    <View style={styles.stageRow}>
      {returnStages.map((stage, index) => {
        const done = terminalRefunded || (currentIndex >= 0 && index < currentIndex);
        const active = stage.key === status;
        return (
          <View key={stage.key} style={styles.stageItem}>
            <View
              style={[
                styles.stageDot,
                {
                  backgroundColor: done ? c.success : active ? c.primary : c.surfaceAlt,
                  borderColor: done ? c.success : active ? c.primary : c.border,
                },
              ]}
            >
              {done ? <Ionicons name="checkmark" size={11} color={c.textInverse} /> : <Text variant="label" color={active ? c.textInverse : c.textMuted}>{index + 1}</Text>}
            </View>
            <Text variant="caption" color={active ? c.text : c.textMuted} style={styles.stageLabel}>{stage.label}</Text>
          </View>
        );
      })}
    </View>
  );
}

function RefundHistory({
  saleId,
  refunds,
  busy,
  runMutation,
}: {
  saleId: string;
  refunds: Refund[];
  busy: boolean;
  runMutation: MutationRunner;
}) {
  const { colors: c } = useAppTheme();
  if (refunds.length === 0) {
    return null;
  }

  function confirmVoid(refundId: string) {
    Alert.alert(
      'Void refund?',
      'This removes the refund event and recalculates the sale.',
      [
        { text: 'Cancel', style: 'cancel' },
        { text: 'Void refund', style: 'destructive', onPress: () => { void runMutation(() => ordersApi.voidRefund(saleId, refundId)); } },
      ],
    );
  }

  return (
    <View style={styles.historyStack}>
      {refunds.map((refund) => (
        <View key={refund.id} style={[styles.historyRow, { backgroundColor: c.surface, borderColor: c.borderLight }]}>
          <View style={styles.historyMain}>
            <StatusPill label={refund.type} tone={refund.type === 'Full' ? 'danger' : 'warning'} size="sm" />
            <Text variant="bodyBold">{formatMoney(refund.itemAmount + refund.shippingRefunded)}</Text>
            {refund.itemReturned ? <Text variant="caption" color={c.textMuted}>restocked</Text> : null}
          </View>
          <Text variant="caption" color={c.textMuted} numberOfLines={2}>{day(refund.refundedAt)} · {refund.reason}</Text>
          <Pressable
            onPress={() => confirmVoid(refund.id)}
            disabled={busy}
            hitSlop={8}
            style={styles.voidButton}
          >
            <Text variant="caption" color={c.danger}>Void</Text>
          </Pressable>
        </View>
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  lineRow: { gap: spacing.xs, borderTopWidth: 1, paddingTop: spacing.sm, marginTop: spacing.xs },
  linePress: { gap: spacing.xxs, borderRadius: radius.md },
  lineHead: { flexDirection: 'row', alignItems: 'flex-start', gap: spacing.sm },
  lineRight: { alignItems: 'flex-end', gap: spacing.xxs },
  lineMeta: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.md },
  workspace: { borderRadius: radius.lg, borderWidth: 1, padding: spacing.md, gap: spacing.md },
  formCard: { borderRadius: radius.lg, borderWidth: 1, padding: spacing.md, gap: spacing.sm },
  formHeader: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: spacing.md },
  formStack: { gap: spacing.sm },
  advancedGrid: { gap: spacing.sm },
  historyStack: { gap: spacing.sm },
  historyRow: { borderWidth: 1, borderRadius: radius.md, padding: spacing.sm, gap: spacing.xs },
  historyMain: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  voidButton: { alignSelf: 'flex-start', paddingVertical: spacing.xs },
  returnHistory: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs },
  returnRefundBox: { borderRadius: radius.md, padding: spacing.sm, gap: spacing.sm },
  exceptionBox: { borderTopWidth: 1, paddingTop: spacing.sm, gap: spacing.sm },
  stageRow: { flexDirection: 'row', gap: spacing.xs },
  stageItem: { flex: 1, alignItems: 'center', gap: spacing.xs },
  stageDot: { width: 24, height: 24, borderRadius: radius.full, borderWidth: 1, alignItems: 'center', justifyContent: 'center' },
  stageLabel: { textAlign: 'center' },
  toggleRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, borderWidth: 1, borderRadius: radius.md, padding: spacing.sm },
  actions: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm, marginTop: spacing.xs, alignItems: 'center' },
});
