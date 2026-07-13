class CashboxQueue {
  constructor() {
    this.tails = new Map();
  }

  enqueue(cashboxUniqueNumber, task) {
    if (!cashboxUniqueNumber) throw new Error('cashboxUniqueNumber is required');
    if (typeof task !== 'function') throw new Error('queue task must be a function');

    const previous = this.tails.get(cashboxUniqueNumber) || Promise.resolve();
    const next = previous.then(task, task);
    const tail = next.finally(() => {
      if (this.tails.get(cashboxUniqueNumber) === tail) {
        this.tails.delete(cashboxUniqueNumber);
      }
    }).catch(() => {});

    this.tails.set(cashboxUniqueNumber, tail);

    return next;
  }
}

module.exports = {
  CashboxQueue,
};
