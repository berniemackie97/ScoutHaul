import {
  inventoryStatusTone,
  listingReadinessTone,
  listingStatusTone,
  orderStatusTone,
  taskStatusTone,
} from './statusTone';

describe('inventoryStatusTone', () => {
  it('calls finished states done', () => {
    expect(inventoryStatusTone('Sold')).toBe('success');
    expect(inventoryStatusTone('Shipped')).toBe('success');
    expect(inventoryStatusTone('Delivered')).toBe('success');
    expect(inventoryStatusTone('Packed')).toBe('success');
  });

  it('calls every state that still owes work the same thing', () => {
    // The drift this file exists to prevent: ReadyToList was amber here and green in the listing
    // queue, so one item changed colour depending on which screen you were on.
    for (const status of [
      'Researching',
      'NeedsCleaning',
      'NeedsTesting',
      'NeedsRepair',
      'NeedsPhotos',
      'ReadyToList',
    ]) {
      expect(inventoryStatusTone(status)).toBe('warning');
    }
  });

  it('calls in-flight states in flight', () => {
    expect(inventoryStatusTone('Listed')).toBe('info');
    expect(inventoryStatusTone('Purchased')).toBe('info');
    expect(inventoryStatusTone('Bundled')).toBe('info');
  });

  it('calls states that went wrong wrong', () => {
    expect(inventoryStatusTone('Returned')).toBe('danger');
    expect(inventoryStatusTone('Disposed')).toBe('danger');
  });

  it('says nothing about states with nothing to say, including ones it has never met', () => {
    expect(inventoryStatusTone('Draft')).toBe('neutral');
    expect(inventoryStatusTone('Watchlist')).toBe('neutral');
    expect(inventoryStatusTone('')).toBe('neutral');
    expect(inventoryStatusTone('SomethingAddedLater')).toBe('neutral');
  });
});

describe('listingReadinessTone', () => {
  it('answers a different question: ready to list, or blocked', () => {
    expect(listingReadinessTone('ReadyToList')).toBe('success');
    expect(listingReadinessTone('NeedsPhotos')).toBe('warning');
    expect(listingReadinessTone('NeedsTesting')).toBe('warning');
    expect(listingReadinessTone('NeedsCleaning')).toBe('warning');
  });

  it('stays quiet about items the queue is not asking about', () => {
    expect(listingReadinessTone('Sold')).toBe('neutral');
    expect(listingReadinessTone('Listed')).toBe('neutral');
  });

  it('differs from inventoryStatusTone on exactly one owes-work status, on purpose', () => {
    // Both functions have an opinion about the statuses that still owe work. ReadyToList is the one
    // they are meant to disagree on — it is the queue's whole point. Any other disagreement in this
    // set is drift, which is what put the same item in two different colours before.
    const owesWork = [
      'Researching',
      'NeedsCleaning',
      'NeedsTesting',
      'NeedsRepair',
      'NeedsPhotos',
      'ReadyToList',
    ];
    const disagreements = owesWork.filter(
      (status) => listingReadinessTone(status) !== inventoryStatusTone(status),
    );
    expect(disagreements).toEqual(['ReadyToList']);
  });
});

describe('orderStatusTone', () => {
  it('maps the order lifecycle', () => {
    expect(orderStatusTone('Delivered')).toBe('success');
    expect(orderStatusTone('Cancelled')).toBe('danger');
    expect(orderStatusTone('Refunded')).toBe('danger');
    expect(orderStatusTone('Pending')).toBe('warning');
    expect(orderStatusTone('Paid')).toBe('info');
    expect(orderStatusTone('Fulfilling')).toBe('info');
    expect(orderStatusTone('Shipped')).toBe('info');
  });
});

describe('listingStatusTone', () => {
  it('maps the channel-listing lifecycle', () => {
    expect(listingStatusTone('Active')).toBe('success');
    expect(listingStatusTone('Sold')).toBe('info');
    expect(listingStatusTone('Draft')).toBe('warning');
    expect(listingStatusTone('Cancelled')).toBe('danger');
    expect(listingStatusTone('Ended')).toBe('neutral');
  });
});

describe('taskStatusTone', () => {
  it('maps the task lifecycle', () => {
    expect(taskStatusTone('Completed')).toBe('success');
    expect(taskStatusTone('Blocked')).toBe('danger');
    expect(taskStatusTone('InProgress')).toBe('info');
    expect(taskStatusTone('Open')).toBe('neutral');
    expect(taskStatusTone('Cancelled')).toBe('neutral');
  });
});
