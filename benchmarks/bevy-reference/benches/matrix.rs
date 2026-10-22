use std::hint::black_box;

use bevy_reference::matrix::{Filters, populate};
use bevy_reference::{Scheduled, for_each};
use criterion::{BenchmarkId, Criterion, Throughput, criterion_group, criterion_main};

fn traversal(c: &mut Criterion) {
    let mut group = c.benchmark_group("traversal");
    for count in [1_000, 100_000, 1_000_000] {
        group.throughput(Throughput::Elements(count as u64));
        for archetypes in [1, 16] {
            for wide in [false, true] {
                let (world, _) = populate(count, archetypes, wide);
                let mut scheduled = Scheduled::from_world(world, for_each);
                let payload = if wide { 256 } else { 0 };
                let name = format!("archetypes_{archetypes}_payload_{payload}");
                group.bench_function(BenchmarkId::new(name, count), |b| {
                    b.iter(|| black_box(&mut scheduled).run())
                });
            }
        }
    }
    group.finish();
}

type FilterCase = (&'static str, fn(&mut Filters));

// Each case gets a fresh world, as BenchmarkDotNet runs each case's global setup in its own process.
fn filters(c: &mut Criterion) {
    let mut group = c.benchmark_group("filters");
    let cases: [FilterCase; 4] = [
        ("tracked_write", Filters::tracked_write),
        ("changed_none", Filters::changed_none),
        ("write_then_changed_all", Filters::write_then_changed_all),
        ("optional_half_present", Filters::optional_half_present),
    ];
    for count in [1_000, 100_000] {
        group.throughput(Throughput::Elements(count as u64));
        for (name, case) in cases {
            let mut filters = Filters::new(count);
            group.bench_function(BenchmarkId::new(name, count), |b| {
                b.iter(|| case(black_box(&mut filters)))
            });
        }
    }
    group.finish();
}

criterion_group!(benches, traversal, filters);
criterion_main!(benches);
