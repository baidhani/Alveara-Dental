const real = Date.now;
Date.now = () => real() + 12000000;
