import axios from 'axios';

export const processError = (error: unknown, fallback: (message: string | undefined) => void) => {
  if (axios.isAxiosError<string>(error)) {
    fallback(error.response?.data);
  }
};
