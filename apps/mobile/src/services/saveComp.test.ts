import { ApiError } from '../api/apiError';
import { api } from '../api';
import { enqueueComp } from '../db/localStore';
import { describeCompOutcome, saveComp } from './saveComp';

jest.mock('../api', () => ({
  api: { addComp: jest.fn() },
}));
jest.mock('../db/localStore', () => ({ enqueueComp: jest.fn() }));

const mockAdd = api.addComp as jest.Mock;
const mockEnqueue = enqueueComp as jest.Mock;

const body = { title: 'Nike Air Max', price: 60 } as never;

beforeEach(() => jest.clearAllMocks());

describe('saveComp', () => {
  it('reports a plain save', async () => {
    mockAdd.mockResolvedValue({});
    await expect(saveComp(body)).resolves.toEqual({ status: 'saved' });
    expect(mockEnqueue).not.toHaveBeenCalled();
  });

  it('keeps the comp on the phone when there is no connection', async () => {
    mockAdd.mockRejectedValue(new ApiError({ reason: 'offline', message: 'offline' }));

    await expect(saveComp(body)).resolves.toEqual({ status: 'queued', because: 'offline' });
    expect(mockEnqueue).toHaveBeenCalledWith(body);
  });

  it('keeps the comp when the server does not answer', async () => {
    mockAdd.mockRejectedValue(new ApiError({ reason: 'timeout', message: 'slow' }));

    await expect(saveComp(body)).resolves.toEqual({ status: 'queued', because: 'unreachable' });
    expect(mockEnqueue).toHaveBeenCalledWith(body);
  });

  it('does not queue something the server rejected', async () => {
    mockAdd.mockRejectedValue(
      new ApiError({ reason: 'http', status: 400, message: 'Price must be positive.' }),
    );

    await expect(saveComp(body)).resolves.toEqual({
      status: 'rejected',
      message: 'Price must be positive.',
    });
    expect(mockEnqueue).not.toHaveBeenCalled();
  });

  it('does not queue when the user is signed out', async () => {
    mockAdd.mockRejectedValue(new ApiError({ reason: 'unauthenticated', message: 'Sign in.' }));

    await expect(saveComp(body)).resolves.toMatchObject({ status: 'rejected' });
    expect(mockEnqueue).not.toHaveBeenCalled();
  });
});

describe('describeCompOutcome', () => {
  it('does not describe a queued comp as a failure', () => {
    const offline = describeCompOutcome({ status: 'queued', because: 'offline' });
    expect(offline).toMatch(/saved/i);
    expect(offline).not.toMatch(/fail|error|could not/i);
  });

  it('passes a rejection through verbatim so the user sees the real reason', () => {
    expect(describeCompOutcome({ status: 'rejected', message: 'Price must be positive.' })).toBe(
      'Price must be positive.',
    );
  });
});
