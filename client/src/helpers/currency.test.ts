import { describe, expect, it } from 'vitest';
import { formatGBP, isPenceAmount, MAX_MONEY_AMOUNT, MONEY_PATTERN, normalizeAmount, parseAmount, readAmount } from './currency';

// The input rules mirror the server's MoneyAnswer, and the case lists below are copied from
// api/tests/Application.Tests.Unit/Validation/MoneyAnswerTests.cs. Nothing links the two suites, so a rule changed
// on one side has to be copied to the other by hand: an amount this client accepts and the API refuses comes back
// to the user as a 422. The refusal messages are pinned in views/Activities/answers.test.ts.

const WHOLE_POUNDS: [string, number][] = [
  ['1', 1],
  ['1500', 1500],
  ['1,500', 1500],
  ['25 000', 25000],
  ['25 000', 25000],
  ['1,000,000', 1000000],
  ['1000000000', 1000000000],
  ['1,000,000,000', 1000000000],
];

const DECIMALS = ['10.50', '5,10', '0,10', '0,100', '1,5000', '1,500.50', '1,500,50', '25 000,5'];

const NOT_NUMBERS = ['lots', '5 10', '12,34,56', '1,,500', ',500', '1,500,', '-5', '- 5', '-1,500'];

describe('formatGBP', () => {
  it.each([
    [0, '£0'],
    [1500, '£1,500'],
    [1000000, '£1,000,000'],
    [1234.567, '£1,234.57'],
    [1.5, '£1.5'],
  ])('formats %s as %s', (amount, expected) => {
    expect(formatGBP(amount)).toBe(expected);
  });

  it('puts the sign outside the symbol', () => {
    expect(formatGBP(-1500)).toBe('-£1,500');
    expect(formatGBP(-0.5)).toBe('-£0.5');
  });

  it('renders no amount as an em dash', () => {
    expect(formatGBP(null)).toBe('—');
  });
});

describe('normalizeAmount', () => {
  it.each([
    ['1,500', '1500'],
    [' 25 000 ', '25000'],
    ['25 000', '25000'],
    ['1,000,000', '1000000'],
    ['1500', '1500'],
    ['£1,500', '1500'],
    ['£ 1500', '1500'],
  ])('reads through the thousands separators in %j', (value, expected) => {
    expect(normalizeAmount(value)).toBe(expected);
  });

  it.each([
    ['5,10', '5,10'],
    ['0,100', '0,100'],
    ['12.5', '12.5'],
    ['12,34,56', '12,34,56'],
    ['5 10', '5 10'],
    ['1,500,', '1,500,'],
  ])('hands back %j as typed, because its separator is not a thousands separator', (value, expected) => {
    expect(normalizeAmount(value)).toBe(expected);
  });

  it('treats a missing value as blank', () => {
    expect(normalizeAmount(undefined as unknown as string)).toBe('');
    expect(normalizeAmount('  £ ')).toBe('');
  });
});

describe('isPenceAmount', () => {
  it.each(DECIMALS)('recognises %j as an amount with pence', (value) => {
    expect(isPenceAmount(normalizeAmount(value))).toBe(true);
  });

  it.each([...WHOLE_POUNDS.map(([value]) => value), ...NOT_NUMBERS, '.5', '5.'])('does not call %j pence', (value) => {
    expect(isPenceAmount(normalizeAmount(value))).toBe(false);
  });
});

describe('parseAmount', () => {
  it.each(WHOLE_POUNDS)('reads %j as %s', (value, expected) => {
    expect(parseAmount(value)).toBe(expected);
  });

  it('reads 5,10 as nothing, never as 510 (BLUM-8955)', () => {
    expect(parseAmount('5,10')).toBeNull();
  });

  it.each([...DECIMALS, ...NOT_NUMBERS, '.5', '5.', '', '   '])('reads %j as nothing', (value) => {
    expect(parseAmount(value)).toBeNull();
  });

  it.each([
    ['0', 0],
    ['000', 0],
  ])('reads %j as %s, leaving the minimum to the caller', (value, expected) => {
    expect(parseAmount(value)).toBe(expected);
  });

  it('strips a typed £ so a draft save stores the number on screen', () => {
    expect(parseAmount('£1,500')).toBe(1500);
  });
});

describe('readAmount', () => {
  it.each([
    ['1500', 1500],
    ['1,500', 1500],
    ['25 000', 25000],
    ['-40', -40],
    ['- 5', -5],
    ['-1,500', -1500],
    ['+1,500', 1500],
    ['1,500.50', 1500.5],
    ['-1 500.5', -1500.5],
    ['0.001', 0.001],
    ['£1,500', 1500],
  ])('reads the stored amount %j as %s', (value, expected) => {
    expect(readAmount(value)).toBe(expected);
  });

  it.each(['', '5,10', '0,100', '1,5000', '1 5 0 0', '1,500.5,0', '1500.50,000', '--5', 'lots'])('does not read %j', (value) => {
    expect(readAmount(value)).toBeNull();
  });

  it('does not read a digit string long enough to overflow to Infinity', () => {
    expect(readAmount('9'.repeat(400))).toBeNull();
  });
});

describe('the shared money rules', () => {
  it('accepts whole pounds only', () => {
    expect(MONEY_PATTERN.test('1500')).toBe(true);
    expect(MONEY_PATTERN.test('0')).toBe(true);
    expect(MONEY_PATTERN.test('1,500')).toBe(false);
    expect(MONEY_PATTERN.test('15.00')).toBe(false);
    expect(MONEY_PATTERN.test('-1')).toBe(false);
  });

  it('caps an amount where FundingSpendValidator and ActivityAnswerValidator do', () => {
    expect(MAX_MONEY_AMOUNT).toBe(1_000_000_000);
  });
});
