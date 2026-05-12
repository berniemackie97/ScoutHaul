import type {
  SourcingRouteDetail,
  SourcingStopPriority,
  SourcingStopSourceType,
  SourcingTripStop,
} from './types';

export const sourceTypeOptions: { value: SourcingStopSourceType; label: string }[] = [
  { value: 'GarageSale', label: 'Garage sale' },
  { value: 'EstateSale', label: 'Estate sale' },
  { value: 'ThriftStore', label: 'Thrift store' },
  { value: 'Goodwill', label: 'Goodwill' },
  { value: 'FacebookPickup', label: 'Facebook pickup' },
  { value: 'Auction', label: 'Auction' },
  { value: 'Liquidation', label: 'Liquidation' },
  { value: 'FleaMarket', label: 'Flea market' },
  { value: 'Other', label: 'Other' },
];

export const priorityOptions: { value: SourcingStopPriority; label: string }[] = [
  { value: 'Low', label: 'Low' },
  { value: 'Medium', label: 'Medium' },
  { value: 'High', label: 'High' },
  { value: 'Critical', label: 'Critical' },
];

export const priorityRank: Record<SourcingStopPriority, number> = {
  Low: 1,
  Medium: 2,
  High: 3,
  Critical: 4,
};

export function sourceTypeLabel(sourceType: SourcingStopSourceType): string {
  return sourceTypeOptions.find((item) => item.value === sourceType)?.label ?? sourceType;
}

export function orderedStops(stops: SourcingTripStop[]): SourcingTripStop[] {
  return [...stops].sort((a, b) => a.plannedOrder - b.plannedOrder);
}

export function getCurrentStop(route: SourcingRouteDetail): SourcingTripStop | null {
  return orderedStops(route.stops).find((stop) => !isResolvedStop(stop)) ?? null;
}

export function isResolvedStop(stop: SourcingTripStop): boolean {
  return stop.status === 'Completed' || stop.status === 'Skipped' || stop.status === 'Cancelled';
}

export function formatMinutes(minutes: number): string {
  const abs = Math.abs(Math.round(minutes));
  if (abs < 60) return `${abs} min`;
  const hours = Math.floor(abs / 60);
  const mins = abs % 60;
  return mins === 0 ? `${hours} hr` : `${hours} hr ${mins} min`;
}

export type ScheduleInsight = {
  varianceMinutes: number;
  statusText: string;
  statusTone: 'success' | 'warning' | 'danger' | 'neutral';
  guidance: string;
  stopRemainingMinutes: number | null;
  atRiskStop: SourcingTripStop | null;
  skipCandidate: SourcingTripStop | null;
};

export function getScheduleInsight(route: SourcingRouteDetail, now = new Date()): ScheduleInsight {
  const stops = orderedStops(route.stops);
  const currentStop = getCurrentStop(route);
  const activeIndex = currentStop ? stops.findIndex((stop) => stop.id === currentStop.id) : stops.length;
  const completedBeforeCurrent = stops.slice(0, Math.max(activeIndex, 0)).filter((stop) => stop.status === 'Completed');
  const plannedBeforeCurrent = completedBeforeCurrent.reduce((sum, stop) => sum + stop.estimatedDurationMinutes, 0);
  const actualBeforeCurrent = completedBeforeCurrent.reduce((sum, stop) => sum + actualStopMinutes(stop), 0);

  let stopRemainingMinutes: number | null = null;
  let varianceMinutes = actualBeforeCurrent - plannedBeforeCurrent;

  if (currentStop?.status === 'InProgress' && currentStop.actualArrivalAt) {
    const elapsed = minutesBetween(new Date(currentStop.actualArrivalAt), now);
    stopRemainingMinutes = currentStop.estimatedDurationMinutes - elapsed;
    varianceMinutes = actualBeforeCurrent + elapsed - (plannedBeforeCurrent + currentStop.estimatedDurationMinutes);
  } else if (currentStop && currentStop.plannedArrivalAt) {
    varianceMinutes = Math.max(varianceMinutes, minutesBetween(new Date(currentStop.plannedArrivalAt), now));
  }

  const atRiskStop = findAtRiskStop(route, currentStop, varianceMinutes, now);
  const skipCandidate = atRiskStop ? findSkipCandidate(stops, currentStop, atRiskStop) : null;
  const statusText = Math.round(varianceMinutes) > 1
    ? `You are ${formatMinutes(varianceMinutes)} behind schedule.`
    : Math.round(varianceMinutes) < -1
      ? `You are ${formatMinutes(varianceMinutes)} ahead of schedule.`
      : 'You are on schedule.';

  let guidance = 'Next stop is flexible. Continue.';
  if (atRiskStop && skipCandidate) {
    guidance = `You are behind, and Stop ${atRiskStop.plannedOrder} has a hard time window. Consider skipping Stop ${skipCandidate.plannedOrder}.`;
  } else if (atRiskStop) {
    guidance = `Stop ${atRiskStop.plannedOrder} is at risk. Continue anyway, skip a lower-priority stop, or reorder manually.`;
  } else if (currentStop?.status === 'InProgress' && stopRemainingMinutes !== null && stopRemainingMinutes < 0) {
    guidance = 'You are over the planned time for this stop. Complete it or skip the next flexible stop.';
  } else if (!currentStop) {
    guidance = 'Route complete. Review the summary before closing out.';
  }

  return {
    varianceMinutes,
    statusText,
    statusTone: varianceMinutes > 10 || atRiskStop ? 'danger' : varianceMinutes > 1 ? 'warning' : 'success',
    guidance,
    stopRemainingMinutes,
    atRiskStop,
    skipCandidate,
  };
}

export function getBudgetGuidance(route: SourcingRouteDetail): { text: string; tone: 'success' | 'warning' | 'danger' | 'neutral'; ratio: number } {
  const budget = route.summary.cashBudget ?? 0;
  if (budget <= 0) {
    return { text: 'No route budget set.', tone: 'neutral', ratio: 0 };
  }

  const spent = route.summary.totalPurchaseCost;
  const ratio = spent / budget;
  if (route.summary.remainingBudget < 0) {
    return { text: 'Route budget exceeded.', tone: 'danger', ratio };
  }

  if (ratio >= 0.8) {
    return { text: "You have used 80% of today's route budget.", tone: 'warning', ratio };
  }

  return { text: 'Route budget is on track.', tone: 'success', ratio };
}

function findAtRiskStop(
  route: SourcingRouteDetail,
  currentStop: SourcingTripStop | null,
  varianceMinutes: number,
  now: Date,
): SourcingTripStop | null {
  const stops = orderedStops(route.stops);
  const activeIndex = currentStop ? stops.findIndex((stop) => stop.id === currentStop.id) : stops.length;
  let projected = new Date(now);

  if (currentStop?.status === 'InProgress' && currentStop.actualArrivalAt) {
    const remaining = currentStop.estimatedDurationMinutes - minutesBetween(new Date(currentStop.actualArrivalAt), now);
    projected = addMinutes(projected, Math.max(remaining, 0));
  }

  for (let index = Math.max(activeIndex + 1, 0); index < stops.length; index++) {
    const stop = stops[index];
    if (isResolvedStop(stop)) continue;

    if (stop.isTimeSensitive) {
      if (stop.timeWindowEndAt && projected > new Date(stop.timeWindowEndAt)) {
        return stop;
      }

      if (stop.plannedArrivalAt && minutesBetween(new Date(stop.plannedArrivalAt), projected) > 5) {
        return stop;
      }

      if (!stop.timeWindowEndAt && !stop.plannedArrivalAt && varianceMinutes > 5) {
        return stop;
      }
    }

    projected = addMinutes(projected, stop.estimatedDurationMinutes);
  }

  return null;
}

function findSkipCandidate(
  stops: SourcingTripStop[],
  currentStop: SourcingTripStop | null,
  atRiskStop: SourcingTripStop,
): SourcingTripStop | null {
  return stops
    .filter((stop) => {
      const afterCurrent = currentStop ? stop.plannedOrder > currentStop.plannedOrder : true;
      return afterCurrent
        && stop.plannedOrder < atRiskStop.plannedOrder
        && stop.canBeSkipped
        && !isResolvedStop(stop)
        && priorityRank[stop.priority] < priorityRank[atRiskStop.priority];
    })
    .sort((a, b) => priorityRank[a.priority] - priorityRank[b.priority] || a.plannedOrder - b.plannedOrder)[0] ?? null;
}

function actualStopMinutes(stop: SourcingTripStop): number {
  if (!stop.actualArrivalAt || !stop.actualDepartureAt) return 0;
  return Math.max(0, minutesBetween(new Date(stop.actualArrivalAt), new Date(stop.actualDepartureAt)));
}

function minutesBetween(start: Date, end: Date): number {
  return (end.getTime() - start.getTime()) / 60000;
}

function addMinutes(date: Date, minutes: number): Date {
  return new Date(date.getTime() + minutes * 60000);
}
