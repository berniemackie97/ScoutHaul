import { parseOptionalValidatedNumber } from './validation';

const parse = (value: string, rules?: Parameters<typeof parseOptionalValidatedNumber>[2]) =>
  parseOptionalValidatedNumber(value, 'Purchase price', rules);

describe('parseOptionalValidatedNumber', () => {
  it('accepts a blank field, because these are optional', () => {
    // An empty box must not read as an error — most of this form is optional.
    expect(parse('')).toEqual({ value: null, error: null });
    expect(parse('   ')).toEqual({ value: null, error: null });
  });

  it('reads a number, ignoring surrounding space', () => {
    expect(parse('12.5')).toEqual({ value: 12.5, error: null });
    expect(parse('  40  ')).toEqual({ value: 40, error: null });
    expect(parse('0')).toEqual({ value: 0, error: null });
  });

  it('refuses something that is not a number, and names the field', () => {
    // Silently dropping a mistyped price is worse than saying so.
    const result = parse('abc');
    expect(result.value).toBeNull();
    expect(result.error).toBe('Purchase price must be a valid number.');
  });

  it('refuses infinity, which Number() will happily produce', () => {
    expect(parse('Infinity').error).toContain('valid number');
  });

  it('enforces a minimum', () => {
    expect(parse('-1', { min: 0 }).error).toBe('Purchase price must be at least 0.');
    expect(parse('0', { min: 0 })).toEqual({ value: 0, error: null });
  });

  it('enforces a maximum', () => {
    expect(parse('3000', { max: 2100 }).error).toBe('Purchase price must be 2100 or less.');
  });

  it('enforces whole numbers where a fraction makes no sense', () => {
    expect(parse('1998.5', { integer: true }).error).toBe('Purchase price must be a whole number.');
    expect(parse('1998', { integer: true })).toEqual({ value: 1998, error: null });
  });

  it('returns no value alongside an error, so a bad field is never saved', () => {
    for (const bad of ['abc', '-1', '5000']) {
      const result = parse(bad, { min: 0, max: 100 });
      if (result.error) expect(result.value).toBeNull();
    }
  });
});
