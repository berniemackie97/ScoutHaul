import { extractBarcode, normConfidence, normRangeType, dedupeQueries, emptyResult } from './identifyHelpers';

describe('extractBarcode', () => {
  it('pulls a valid UPC-A (12) / EAN-13 / EAN-8 / ITF-14 out of noisy text', () => {
    expect(extractBarcode('UPC 036000291452 on the box')).toBe('036000291452'); // 12
    expect(extractBarcode('4006381333931')).toBe('4006381333931');             // 13
    expect(extractBarcode('code 73513537')).toBe('73513537');                  // 8
    expect(extractBarcode('00012345678905')).toBe('00012345678905');           // 14
  });

  it('ignores numbers that are not a valid barcode length', () => {
    expect(extractBarcode('1234567')).toBeNull();        // 7
    expect(extractBarcode('1234567890')).toBeNull();     // 10
    expect(extractBarcode('no digits here')).toBeNull();
    expect(extractBarcode('')).toBeNull();
  });

  it('tolerates whitespace inside the digit run', () => {
    expect(extractBarcode('0 3 6 0 0 0 2 9 1 4 5 2')).toBe('036000291452');
  });
});

describe('normConfidence', () => {
  it('passes through valid levels, nulls everything else', () => {
    expect(normConfidence('high')).toBe('high');
    expect(normConfidence('medium')).toBe('medium');
    expect(normConfidence('low')).toBe('low');
    expect(normConfidence('HIGH')).toBeNull();   // case-sensitive on purpose (server is normalized)
    expect(normConfidence('')).toBeNull();
    expect(normConfidence(undefined)).toBeNull();
    expect(normConfidence(null)).toBeNull();
  });
});

describe('normRangeType', () => {
  it('passes through valid types, nulls everything else', () => {
    expect(normRangeType('tight')).toBe('tight');
    expect(normRangeType('moderate')).toBe('moderate');
    expect(normRangeType('wide')).toBe('wide');
    expect(normRangeType('huge')).toBeNull();
    expect(normRangeType(null)).toBeNull();
  });
});

describe('dedupeQueries', () => {
  it('drops the primary, case-insensitive dups, and sub-3-char noise; caps at 4', () => {
    const out = dedupeQueries('Sony WH-1000XM4', [
      'Sony WH-1000XM4',        // == primary → dropped
      'sony wh-1000xm4',        // case dup of primary → dropped
      'Sony headphones',
      'Sony headphones',        // dup → dropped
      'XM',                     // < 3 chars → dropped
      'noise cancelling',
      'wireless over-ear',
      'fifth extra query',      // beyond cap of 4
    ]);
    expect(out).toEqual(['Sony headphones', 'noise cancelling', 'wireless over-ear']);
    expect(out.length).toBeLessThanOrEqual(4);
  });

  it('handles empty candidates', () => {
    expect(dedupeQueries('anything', [])).toEqual([]);
  });
});

describe('emptyResult', () => {
  it('is a fully-null, not-identified result (no fabricated identity/confidence)', () => {
    const r = emptyResult();
    expect(r.title).toBeNull();
    expect(r.confidence).toBeNull();
    expect(r.valuationConfidence).toBeNull();
    expect(r.comps).toEqual([]);
    expect(r.alternateQueries).toEqual([]);
    expect(r.searchQuery).toBe('');
  });
});
