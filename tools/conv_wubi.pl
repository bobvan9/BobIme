use strict;
use warnings;
use open qw(:std :encoding(UTF-8));

# 极点 86 码表（rime-wubi86-jidian 的 *.dict.yaml）转成产品用的纯 tsv。
# 用法：perl tools/conv_wubi.pl data/dict/jidian86.tsv <主表> <扩展表...>
my $dst = shift @ARGV;
open my $out, '>', $dst or die "open $dst: $!";

my (%seen, $kept, $dropped);
for my $src (@ARGV) {
    open my $in, '<', $src or die "open $src: $!";
    while (my $line = <$in>) {
        chomp $line;
        $line =~ s/\r//;
        my ($word, $code) = split /\t/, $line;

        # yaml 头、注释和缺列的行都在这一步排掉。
        next unless defined $word && length $word && defined $code && $code =~ /^[A-Za-z]{1,4}$/;
        $word =~ s/^\s+|\s+$//g;
        $code = lc $code;
        next unless length $word;

        # 同一个词同一个码只留第一次出现；主表在前，扩展表补在后面。
        my $key = "$word\t$code";
        if ($seen{$key}++) { $dropped++; next }

        print {$out} "$word\t$code\n";
        $kept++;
    }
    close $in;
}

# 码表内的次序就是候选次序（同码按行号排），所以原文件的「同码按权重降序」要原样保住，不再重排。
print "kept $kept entries, dropped $dropped duplicates\n";
