import { apiRequest } from './client';
import { ordersApi } from './orders';

jest.mock('./client', () => ({ apiRequest: jest.fn(() => Promise.resolve([])) }));

const request = apiRequest as jest.MockedFunction<typeof apiRequest>;

describe('sale candidates', () => {
  beforeEach(() => request.mockClear());

  it('asks the server to search, one page at a time, and passes the cancellation through', async () => {
    const controller = new AbortController();

    await ordersApi.candidates('nikon f3', { signal: controller.signal });

    expect(request).toHaveBeenCalledWith('/api/sales/candidates?search=nikon%20f3&take=20', { signal: controller.signal });
  });

  it('asks for the first page of the most recent items when nothing is typed', async () => {
    await ordersApi.candidates();

    expect(request).toHaveBeenCalledWith('/api/sales/candidates?take=20', {});
  });
});
