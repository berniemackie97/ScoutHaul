import { parseAttributes, serializeAttributes } from './attributes';

describe('parseAttributes', () => {
  it('reads a stored object of strings', () => {
    expect(parseAttributes('{"material":"leather","era":"1970s"}')).toEqual({
      material: 'leather',
      era: '1970s',
    });
  });

  it('treats anything unusable as no attributes rather than crashing the form', () => {
    // The value is free-form text from the API: legacy rows, nulls and hand-edits all land here.
    expect(parseAttributes(null)).toEqual({});
    expect(parseAttributes(undefined)).toEqual({});
    expect(parseAttributes('')).toEqual({});
    expect(parseAttributes('not json')).toEqual({});
    expect(parseAttributes('"a string"')).toEqual({});
    expect(parseAttributes('[1,2,3]')).toEqual({});
    expect(parseAttributes('null')).toEqual({});
  });

  it('keeps only the entries that are strings', () => {
    // A number or nested object would break the text inputs bound to these.
    expect(parseAttributes('{"a":"keep","b":42,"c":null,"d":{"nested":true}}')).toEqual({ a: 'keep' });
  });

  it('reads a stored list as a list and drops an empty one', () => {
    expect(parseAttributes('{"material":["Silk","Cotton",3],"empty":[]}')).toEqual({ material: ['Silk', 'Cotton'] });
  });
});

describe('serializeAttributes', () => {
  it('stores what was filled in', () => {
    expect(serializeAttributes({ material: 'oak' })).toBe('{"material":"oak"}');
  });

  it('trims what it stores', () => {
    expect(serializeAttributes({ material: '  oak  ' })).toBe('{"material":"oak"}');
  });

  it('stores nothing at all when nothing was filled in', () => {
    // Null rather than "{}" so an untouched form leaves the column empty.
    expect(serializeAttributes({})).toBeNull();
    expect(serializeAttributes({ material: '' })).toBeNull();
    expect(serializeAttributes({ material: '   ' })).toBeNull();
  });

  it('drops the blanks and keeps the rest', () => {
    expect(serializeAttributes({ material: 'oak', era: '  ', finish: 'waxed' })).toBe(
      '{"material":"oak","finish":"waxed"}',
    );
  });

  it('survives a round trip', () => {
    const original = { material: 'leather', era: '1970s' };
    expect(parseAttributes(serializeAttributes(original))).toEqual(original);
  });
});

describe('serializeAttributes with lists', () => {
  it('keeps a list as a list and drops an empty one', () => {
    expect(serializeAttributes({ material: ['Silk', 'Cotton'], lining: [], department: ' Women ' })).toBe(
      '{"material":["Silk","Cotton"],"department":"Women"}',
    );
  });
});
